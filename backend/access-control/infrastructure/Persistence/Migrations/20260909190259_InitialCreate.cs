using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Playground.AccessControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "authorization_models",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchemaVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RawDsl = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authorization_models", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "decision_audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Relation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Object = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Allowed = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    PathJson = table.Column<string>(type: "jsonb", nullable: true),
                    ModelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EvaluationMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DurationMs = table.Column<double>(type: "double precision", nullable: false),
                    StoreQueries = table.Column<int>(type: "integer", nullable: false),
                    Origin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "relationship_tuples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ObjectType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ObjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Relation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubjectRelation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relationship_tuples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_authorization_models_sequence",
                table: "authorization_models",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_decision_audit_object",
                table: "decision_audit",
                column: "Object");

            migrationBuilder.CreateIndex(
                name: "ix_decision_audit_subject",
                table: "decision_audit",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "ix_decision_audit_timestamp",
                table: "decision_audit",
                column: "Timestamp",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_relationship_tuples_forward",
                table: "relationship_tuples",
                columns: new[] { "ObjectType", "ObjectId", "Relation" });

            migrationBuilder.CreateIndex(
                name: "ix_relationship_tuples_reverse",
                table: "relationship_tuples",
                columns: new[] { "SubjectType", "SubjectId", "SubjectRelation", "Relation", "ObjectType" });

            migrationBuilder.CreateIndex(
                name: "ux_relationship_tuples",
                table: "relationship_tuples",
                columns: new[] { "ObjectType", "ObjectId", "Relation", "SubjectType", "SubjectId", "SubjectRelation" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authorization_models");

            migrationBuilder.DropTable(
                name: "decision_audit");

            migrationBuilder.DropTable(
                name: "relationship_tuples");
        }
    }
}
