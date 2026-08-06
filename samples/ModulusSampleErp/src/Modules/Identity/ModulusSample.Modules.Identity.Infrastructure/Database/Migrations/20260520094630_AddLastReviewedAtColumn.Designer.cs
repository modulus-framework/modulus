using System;
using ModulusSample.Modules.Identity.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ModulusSample.Modules.Identity.Infrastructure.Database.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20260520094630_AddLastReviewedAtColumn")]
partial class AddLastReviewedAtColumn
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasDefaultSchema("identity")
            .HasAnnotation("ProductVersion", "8.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("ModulusSample.Modules.Identity.Domain.Entities.Role", b =>
            {
                b.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<DateTime>("CreatedAtUtc")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at_utc");

                b.Property<string>("Description")
                    .IsRequired()
                    .HasMaxLength(500)
                    .HasColumnType("character varying(500)")
                    .HasColumnName("description");

                b.Property<bool>("IsSystem")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("boolean")
                    .HasDefaultValue(false)
                    .HasColumnName("is_system");

                b.Property<DateTime?>("LastReviewedAtUtc")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("last_reviewed_at_utc");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)")
                    .HasColumnName("name");

                b.Property<long>("Version")
                    .IsConcurrencyToken()
                    .HasColumnType("bigint")
                    .HasColumnName("version");

                b.HasKey("Id");

                b.HasIndex("IsSystem")
                    .HasDatabaseName("ix_roles_is_system");

                b.HasIndex("Name")
                    .IsUnique()
                    .HasDatabaseName("ix_roles_name");

                b.ToTable("roles", "identity");
            });
#pragma warning restore 612, 618
    }
}
