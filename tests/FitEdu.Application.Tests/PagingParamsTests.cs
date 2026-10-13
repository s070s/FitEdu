using FitEdu.Application.Common;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class PagingParamsTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void Normalize_PageBelowOne_BecomesOne(int page, int expected)
    {
        PagingParams.Normalize(page, 10).Page.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, PagingParams.MaxPageSize)]
    public void Normalize_PageSize_IsClampedToTheAllowedRange(int pageSize, int expected)
    {
        PagingParams.Normalize(1, pageSize).PageSize.ShouldBe(expected);
    }

    [Theory]
    [InlineData(PagingParams.MaxPage, PagingParams.MaxPage)]
    [InlineData(PagingParams.MaxPage + 1, PagingParams.MaxPage)]
    [InlineData(int.MaxValue, PagingParams.MaxPage)]
    public void Normalize_HugePage_IsCappedAtTheMaximum(int page, int expected)
    {
        PagingParams.Normalize(page, PagingParams.MaxPageSize).Page.ShouldBe(expected);
    }

    [Fact]
    public void Normalize_LargestPage_StillSkipsWithoutOverflow()
    {
        // The services skip (page - 1) * pageSize rows; an unbounded page overflows int and EF rejects a negative Skip.
        var (page, pageSize) = PagingParams.Normalize(int.MaxValue, int.MaxValue);

        var rowsToSkip = checked((page - 1) * pageSize);

        rowsToSkip.ShouldBeGreaterThan(0);
    }
}
