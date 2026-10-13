using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using FitEdu.Domain.Enums;
using FitEdu.Application.DTOs;
using FitEdu.TestSupport;
using FitEdu.Web.Consumers;
using FitEdu.Web.Interfaces;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Unit tests for the typed API consumers (and the shared <c>ApiClientBase</c> helpers)
/// using a stubbed HttpMessageHandler — no server involved.
/// </summary>
public class ConsumerTests
{
    // A no-op token provider for consumer tests: the stubbed handler ignores auth, and an
    // anonymous principal yields a null token (the IAuthService substitute returns null).
    internal static ApiTokenProvider TokenProvider()
    {
        var authState = Substitute.For<AuthenticationStateProvider>();
        authState.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
        return new ApiTokenProvider(authState, Substitute.For<IAuthService>());
    }

    // ---- CourseConsumer ------------------------------------------------------

    [Fact]
    public async Task CourseConsumer_GetCourses_Success_MapsPagedResultAndUrl()
    {
        var body = new PagedResult<CourseListItemDto>(
            new List<CourseListItemDto> { new(Guid.NewGuid(), "Course A", null, false, 3, SubscriptionTier.Free) },
            1, 1, 10);
        var (client, handler) = TestHttp.Json(HttpStatusCode.OK, body);
        var consumer = new CourseConsumer(client, TokenProvider());

        var result = await consumer.GetCoursesAsync(page: 2, pageSize: 5, lang: "el");

        result.Items.Count.ShouldBe(1);
        result.Items[0].Title.ShouldBe("Course A");
        handler.LastRequest!.RequestUri!.PathAndQuery.ShouldContain("api/courses");
        handler.LastRequest.RequestUri.Query.ShouldContain("page=2");
    }

    [Fact]
    public async Task CourseConsumer_GetCourses_SendsCategorySearchGoalLevelAndEquipmentFilters()
    {
        var (client, handler) = TestHttp.Json(HttpStatusCode.OK,
            new PagedResult<CourseListItemDto>([], 0, 1, 10));
        var consumer = new CourseConsumer(client, TokenProvider());
        var categoryId = Guid.NewGuid();

        await consumer.GetCoursesAsync(categoryId: categoryId, search: "core work",
            goal: FitnessGoal.FatLoss, level: CourseLevel.Intermediate, equipment: "dumbbells");

        var query = handler.LastRequest!.RequestUri!.Query;
        query.ShouldContain($"categoryId={categoryId}");
        query.ShouldContain("search=core%20work");
        query.ShouldContain("goal=FatLoss");
        query.ShouldContain("level=Intermediate");
        query.ShouldContain("equipment=dumbbells");
    }

    [Fact]
    public async Task CourseConsumer_GetCourses_WithoutFilters_SendsNoFilterParameters()
    {
        var (client, handler) = TestHttp.Json(HttpStatusCode.OK,
            new PagedResult<CourseListItemDto>([], 0, 1, 10));
        var consumer = new CourseConsumer(client, TokenProvider());

        await consumer.GetCoursesAsync();

        handler.LastRequest!.RequestUri!.Query.ShouldBe("?page=1&pageSize=10&lang=en");
    }

    [Fact]
    public async Task CourseConsumer_GetCourses_ServerError_ReturnsEmptyPagedResult()
    {
        var (client, _) = TestHttp.Json(HttpStatusCode.InternalServerError);
        var consumer = new CourseConsumer(client, TokenProvider());

        var result = await consumer.GetCoursesAsync();

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task CourseConsumer_GetCourse_NotFound_ReturnsNull()
    {
        var (client, _) = TestHttp.Json(HttpStatusCode.NotFound);
        var consumer = new CourseConsumer(client, TokenProvider());

        (await consumer.GetCourseAsync(Guid.NewGuid())).ShouldBeNull();
    }

    [Fact]
    public async Task CourseConsumer_Enroll_Forbidden_StillReadsBody()
    {
        // EnrollAsync deliberately reads the body on 403 (to surface the upgrade message).
        var (client, _) = TestHttp.Json(HttpStatusCode.Forbidden,
            new EnrollmentResultDto(false, "Upgrade required", null));
        var consumer = new CourseConsumer(client, TokenProvider());

        var result = await consumer.EnrollAsync(Guid.NewGuid());

        result!.Success.ShouldBeFalse();
        result.Message.ShouldBe("Upgrade required");
    }

    [Fact]
    public async Task CourseConsumer_CountEnrollments_ReadsTheCount()
    {
        var (client, handler) = TestHttp.Json(HttpStatusCode.OK, 4);
        var consumer = new CourseConsumer(client, TokenProvider());

        (await consumer.CountEnrollmentsAsync()).ShouldBe(4);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/courses/enrolment-count");
    }

    [Fact]
    public async Task CourseConsumer_CountEnrollments_ServerError_ReturnsNull()
    {
        var (client, _) = TestHttp.Json(HttpStatusCode.InternalServerError);
        var consumer = new CourseConsumer(client, TokenProvider());

        (await consumer.CountEnrollmentsAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task CourseConsumer_Enroll_ServerError_ReturnsNull()
    {
        var (client, _) = TestHttp.Json(HttpStatusCode.InternalServerError);
        var consumer = new CourseConsumer(client, TokenProvider());

        (await consumer.EnrollAsync(Guid.NewGuid())).ShouldBeNull();
    }

    [Fact]
    public async Task CourseConsumer_CompleteLesson_Success_ReturnsResult()
    {
        var (client, _) = TestHttp.Json(HttpStatusCode.OK,
            new LessonCompletionResultDto(true, "done", 1, 2, 50.0, false));
        var consumer = new CourseConsumer(client, TokenProvider());

        var result = await consumer.CompleteLessonAsync(Guid.NewGuid());

        result!.CompletedLessons.ShouldBe(1);
        result.CourseCompleted.ShouldBeFalse();
    }

    // ---- BlogConsumer --------------------------------------------------------

    [Fact]
    public async Task BlogConsumer_GetSummaries_Success_ReturnsList()
    {
        var body = new List<BlogArticleSummaryDto>
        {
            new(Guid.NewGuid(), "Title", "slug", "Summary", null, "Author", Array.Empty<string>(), DateTimeOffset.UtcNow)
        };
        var (client, handler) = TestHttp.Json(HttpStatusCode.OK, body);
        var consumer = new BlogConsumer(client, TokenProvider());

        var result = await consumer.GetSummariesAsync(count: 3, lang: "en");

        result!.Count.ShouldBe(1);
        handler.LastRequest!.RequestUri!.Query.ShouldContain("count=3");
    }

    [Fact]
    public async Task BlogConsumer_GetBySlug_NotFound_ReturnsNull()
    {
        var (client, _) = TestHttp.Json(HttpStatusCode.NotFound);
        var consumer = new BlogConsumer(client, TokenProvider());

        (await consumer.GetBySlugAsync("missing")).ShouldBeNull();
    }

    // ---- ExerciseConsumer ----------------------------------------------------

    [Fact]
    public async Task ExerciseConsumer_List_SendsPagingLanguageAndFilters()
    {
        var body = new PagedResult<ExerciseListItemDto>(
            [new(Guid.NewGuid(), "pushups", "Pushups", "strength", "beginner", "body only", ["chest"], "/0.webp", "/1.webp")],
            1, 1, 24);
        var (client, handler) = TestHttp.Json(HttpStatusCode.OK, body);
        var consumer = new ExerciseConsumer(client, TokenProvider());
        var filter = new ExerciseFilterDto(Search: "push up", Muscle: "lower back", Equipment: "body only", Level: "beginner", Category: "strength");

        var result = await consumer.ListAsync(page: 2, pageSize: 12, lang: "el", filter: filter);

        result.Items.ShouldHaveSingleItem().Slug.ShouldBe("pushups");
        var query = handler.LastRequest!.RequestUri!.Query;
        handler.LastRequest.RequestUri.AbsolutePath.ShouldBe("/api/exercises");
        query.ShouldContain("page=2");
        query.ShouldContain("pageSize=12");
        query.ShouldContain("lang=el");
        query.ShouldContain("search=push%20up");
        query.ShouldContain("muscle=lower%20back");
        query.ShouldContain("equipment=body%20only");
        query.ShouldContain("level=beginner");
        query.ShouldContain("category=strength");
    }

    [Fact]
    public async Task ExerciseConsumer_List_WithoutFilter_SendsNoFilterParameters()
    {
        var (client, handler) = TestHttp.Json(HttpStatusCode.OK,
            new PagedResult<ExerciseListItemDto>([], 0, 1, 24));
        var consumer = new ExerciseConsumer(client, TokenProvider());

        await consumer.ListAsync();

        handler.LastRequest!.RequestUri!.Query.ShouldNotContain("muscle=");
        handler.LastRequest.RequestUri.Query.ShouldNotContain("search=");
    }

    [Fact]
    public async Task ExerciseConsumer_List_ServerError_ReturnsEmptyPagedResult()
    {
        var (client, _) = TestHttp.Json(HttpStatusCode.InternalServerError);
        var consumer = new ExerciseConsumer(client, TokenProvider());

        var result = await consumer.ListAsync(page: 3, pageSize: 12);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
        result.Page.ShouldBe(3);
    }

    [Fact]
    public async Task ExerciseConsumer_GetBySlug_NotFound_ReturnsNull()
    {
        var (client, handler) = TestHttp.Json(HttpStatusCode.NotFound);
        var consumer = new ExerciseConsumer(client, TokenProvider());

        (await consumer.GetBySlugAsync("pushups", "el")).ShouldBeNull();
        handler.LastRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/exercises/pushups?lang=el");
    }

    [Fact]
    public async Task ExerciseConsumer_GetFacets_ServerError_ReturnsEmptyFacets()
    {
        var (client, _) = TestHttp.Json(HttpStatusCode.InternalServerError);
        var consumer = new ExerciseConsumer(client, TokenProvider());

        var facets = await consumer.GetFacetsAsync();

        facets.Muscles.ShouldBeEmpty();
        facets.Equipment.ShouldBeEmpty();
        facets.Levels.ShouldBeEmpty();
        facets.Categories.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExerciseConsumer_GetLessonsUsingExercise_ServerError_ReturnsEmptyList()
    {
        var (client, handler) = TestHttp.Json(HttpStatusCode.InternalServerError);
        var consumer = new ExerciseConsumer(client, TokenProvider());

        (await consumer.GetLessonsUsingExerciseAsync("pushups", "el")).ShouldBeEmpty();
        handler.LastRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/exercises/pushups/lessons?lang=el");
    }

    // ---- ApiClientBase auth (C1: authenticate inside the Blazor circuit) -----

    [Fact]
    public async Task ApiClientBase_AttachesBearerToken_FromTokenProvider()
    {
        // The provider mints a token for the current user; every consumer request must carry it
        // as a Bearer header. This is the fix for calls silently 401-ing inside the circuit, where
        // HttpContext (and therefore SsrApiHandler) is unavailable.
        var (client, handler) = TestHttp.Json(HttpStatusCode.OK,
            new PagedResult<CourseListItemDto>(new List<CourseListItemDto>(), 0, 1, 10));

        var authState = Substitute.For<AuthenticationStateProvider>();
        authState.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(
                new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test")))));
        var authService = Substitute.For<IAuthService>();
        authService.GetTokenAsync(Arg.Any<ClaimsPrincipal>()).Returns("test-token-123");

        var consumer = new CourseConsumer(client, new ApiTokenProvider(authState, authService));

        await consumer.GetCoursesAsync();

        handler.LastRequest!.Headers.Authorization.ShouldNotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.ShouldBe("test-token-123");
    }

    [Fact]
    public async Task ApiClientBase_AnonymousUser_SendsNoAuthorizationHeader()
    {
        var (client, handler) = TestHttp.Json(HttpStatusCode.OK,
            new PagedResult<CourseListItemDto>(new List<CourseListItemDto>(), 0, 1, 10));

        // Anonymous principal → provider returns null → no Authorization header is sent.
        var consumer = new CourseConsumer(client, TokenProvider());

        await consumer.GetCoursesAsync();

        handler.LastRequest!.Headers.Authorization.ShouldBeNull();
    }

    // ---- ChatService auth (REST history calls authenticate inside the circuit) ----

    // ChatService does NOT extend ApiClientBase, so it attaches the bearer token itself before each
    // _http REST call. HttpContext is null here (as it is inside a live circuit) — ChatService
    // tolerates that and reads the principal via ApiTokenProvider instead.
    private static ChatService BuildChatService(HttpClient client, ApiTokenProvider tokenProvider) =>
        new(client, Substitute.For<IAuthService>(), tokenProvider,
             Substitute.For<IHttpContextAccessor>(), Substitute.For<ILogger<ChatService>>());

    [Fact]
    public async Task ChatService_RestCall_AttachesBearerToken_FromTokenProvider()
    {
        // Without this the REST history calls 401 inside the circuit (HttpContext/SsrApiHandler null),
        // so conversations/messages silently failed to load when triggered interactively.
        var (client, handler) = TestHttp.Json(HttpStatusCode.OK,
            new PagedResult<ChatConversationDto>(new List<ChatConversationDto>(), 0, 1, 10));

        var authState = Substitute.For<AuthenticationStateProvider>();
        authState.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(
                new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test")))));
        var authService = Substitute.For<IAuthService>();
        authService.GetTokenAsync(Arg.Any<ClaimsPrincipal>()).Returns("chat-token-123");

        var chat = BuildChatService(client, new ApiTokenProvider(authState, authService));

        await chat.GetConversationsAsync();

        handler.LastRequest!.Headers.Authorization.ShouldNotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.ShouldBe("chat-token-123");
    }

    [Fact]
    public async Task ChatService_RestCall_AnonymousUser_SendsNoAuthorizationHeader()
    {
        var (client, handler) = TestHttp.Json(HttpStatusCode.OK,
            new PagedResult<ChatConversationDto>(new List<ChatConversationDto>(), 0, 1, 10));

        var chat = BuildChatService(client, TokenProvider());

        await chat.GetConversationsAsync();

        handler.LastRequest!.Headers.Authorization.ShouldBeNull();
    }
}
