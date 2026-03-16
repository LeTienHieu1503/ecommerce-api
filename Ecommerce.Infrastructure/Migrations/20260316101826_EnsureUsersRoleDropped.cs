using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnsureUsersRoleDropped : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE Name = N'Role'
                      AND Object_ID = Object_ID(N'[Users]')
                )
                BEGIN
                    ALTER TABLE [Users] DROP COLUMN [Role];
                END
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE Name = N'Role'
                      AND Object_ID = Object_ID(N'[Users]')
                )
                BEGIN
                    ALTER TABLE [Users] ADD [Role] nvarchar(max) NOT NULL DEFAULT N'';
                END
                """
            );
        }
    }
}
