using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.Common;
using FitEdu.Web.Controllers;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Every upload endpoint stops its request at the file limit plus room for the form (the server default is 30 MB), so a
/// body far over the limit is refused with a 413 before it is buffered. The in-process test server has no body size
/// limit to enforce (a real Kestrel has), so this runs each action's own filter against a fake limit feature.
/// </summary>
public class UploadRequestSizeLimitTests
{
    public static TheoryData<Type, string, long> UploadActions => new()
    {
        { typeof(ProfileController), nameof(ProfileController.UploadAvatar), UploadLimits.MaxImageBytes },
        { typeof(AdminTestimonialsController), nameof(AdminTestimonialsController.UploadAvatar), UploadLimits.MaxImageBytes },
        { typeof(AdminBlogController), nameof(AdminBlogController.UploadBlogCoverImage), UploadLimits.MaxCoverImageBytes },
        { typeof(SiteSettingsController), nameof(SiteSettingsController.UploadBackgroundImage), UploadLimits.MaxCoverImageBytes },
        { typeof(AdminLessonsController), nameof(AdminLessonsController.UploadPdf), UploadLimits.MaxPdfBytes },
    };

    [Theory]
    [MemberData(nameof(UploadActions))]
    public void UploadAction_CapsTheRequestAtTheFileLimitPlusOneMegabyte(Type controller, string action, long fileLimit)
    {
        var attribute = controller.GetMethod(action)!.GetCustomAttribute<RequestSizeLimitAttribute>();
        attribute.ShouldNotBeNull();
        var limitFeature = new FakeBodySizeLimit();
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(limitFeature);
        var services = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider();
        var filter = (IAuthorizationFilter)attribute.CreateInstance(services);

        // The filter applies its limit only when it is the effective policy, so it is listed as the action's filter.
        filter.OnAuthorization(new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), [(IFilterMetadata)filter]));

        limitFeature.MaxRequestBodySize.ShouldBe(fileLimit + UploadLimits.FormOverheadBytes);
    }

    private sealed class FakeBodySizeLimit : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }
}
