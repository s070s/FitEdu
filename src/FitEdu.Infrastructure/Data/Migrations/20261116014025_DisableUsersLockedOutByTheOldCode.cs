using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitEdu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DisableUsersLockedOutByTheOldCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The old Disable only set the lock-out end date to the far future and left IsEnabled true, which every
            // session check reads. Those members are still signed in or can sign in; turn them off for real. The
            // lock-out date stays as it is. A short lock-out after failed logins ends years before 9999, so it is untouched.
            migrationBuilder.Sql(
                "UPDATE [AspNetUsers] SET [IsEnabled] = 0 " +
                "WHERE [IsEnabled] = 1 AND [LockoutEnd] >= '9999-01-01 00:00:00+00:00';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to how the old code stored a disabled member: enabled, with the far-future lock-out date.
            migrationBuilder.Sql(
                "UPDATE [AspNetUsers] SET [IsEnabled] = 1 " +
                "WHERE [IsEnabled] = 0 AND [LockoutEnd] >= '9999-01-01 00:00:00+00:00';");
        }
    }
}
