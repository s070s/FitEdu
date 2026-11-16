using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.TestSupport;
using FitEdu.Web.Consumers;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class AdminAuditConsumerTests
{
    private static ApiTokenProvider TokenProvider()
    {
        var authState = Substitute.For<AuthenticationStateProvider>();
        authState.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
        return new ApiTokenProvider(authState, Substitute.For<IAuthService>());
    }

    private static (AdminAuditConsumer Consumer, StubHttpMessageHandler Handler) ConsumerAnswering(HttpResponseMessage response)
    {
        var handler = new StubHttpMessageHandler(_ => response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        return (new AdminAuditConsumer(client, TokenProvider()), handler);
    }

    [Fact]
    public async Task GetEntries_AsksForThePageAndReadsTheRows()
    {
        var page = new PagedResult<AdminAuditEntryDto>(
            [new AdminAuditEntryDto(Guid.NewGuid(), "admin-1", "admin@x.test", "member-1", null, AdminAuditAction.AccountDeleted, null, DateTimeOffset.UtcNow)],
            TotalCount: 41, Page: 3, PageSize: 20);
        var (consumer, handler) = ConsumerAnswering(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(page) });

        var result = await consumer.GetEntriesAsync(3, 20);

        handler.LastRequest!.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/admin/audit?page=3&pageSize=20");
        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(41);
        result.Items.ShouldHaveSingleItem().Action.ShouldBe(AdminAuditAction.AccountDeleted);
    }

    [Fact]
    public async Task GetEntries_TheRequestFails_ReturnsNull()
    {
        var (consumer, _) = ConsumerAnswering(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        (await consumer.GetEntriesAsync(1, 20)).ShouldBeNull();
    }
}
