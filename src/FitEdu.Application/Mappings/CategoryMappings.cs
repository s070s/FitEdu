using System.Linq.Expressions;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;

namespace FitEdu.Application.Mappings;

/// <summary>
/// Shared category admin mappers (MAINT-1). AdminCategoryDto was hand-built at three sites in
/// AdminCategoryService (one in-query with computed counts, two after create/update with counts
/// already in hand). Keep the pair's field order in sync.
/// </summary>
public static class CategoryMappings
{
    /// <summary>For IQueryable.Select; counts respect soft-delete like the original query.</summary>
    public static readonly Expression<Func<Category, AdminCategoryDto>> AdminProjection =
        c => new AdminCategoryDto(
            c.Id, c.NameEn, c.NameEl,
            c.Courses.Count(x => !x.IsDeleted),
            c.AssessmentDefinitions.Count(x => !x.IsDeleted),
            c.CreatedAt);

    /// <summary>Materialized variant; counts are supplied by the caller (0 for a new category).</summary>
    public static AdminCategoryDto ToAdminDto(this Category c, int courseCount, int assessmentCount) =>
        new(c.Id, c.NameEn, c.NameEl, courseCount, assessmentCount, c.CreatedAt);
}
