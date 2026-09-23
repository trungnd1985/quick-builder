using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace QuickBuilder.Shared.Infrastructure.Data;

/// <summary>
/// Renames columns, keys and indexes to snake_case, matching data-model.md.
/// </summary>
/// <remarks>
/// PostgreSQL folds unquoted identifiers to lower case, so PascalCase column names end up
/// needing quotes everywhere — in raw SQL, in index filters, in psql sessions an operator
/// runs by hand. snake_case avoids that friction entirely.
/// <para>
/// Hand-rolled rather than taking EFCore.NamingConventions: this is twenty lines against a
/// dependency that would need justifying under Principle XI, and the behaviour is one we
/// want pinned rather than inherited.
/// </para>
/// <para>
/// Table names are set explicitly by each module, so this deliberately leaves them alone.
/// </para>
/// </remarks>
public static class SnakeCaseNaming
{
    public static void UseSnakeCaseColumns(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                // Respect an explicit HasColumnName; only rename the conventional ones.
                if (property.FindAnnotation(RelationalAnnotationNames.ColumnName) is null)
                {
                    property.SetColumnName(ToSnakeCase(property.Name));
                }
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName() ?? string.Empty));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName() ?? string.Empty));
            }

            foreach (var index in entity.GetIndexes())
            {
                var name = index.GetDatabaseName();
                if (!string.IsNullOrEmpty(name))
                {
                    index.SetDatabaseName(ToSnakeCase(name));
                }
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (char.IsUpper(current))
            {
                // Insert a separator at a lower-to-upper boundary (SiteId -> site_id) and
                // at the end of an acronym run (HTTPStatus -> http_status), but never at
                // the start and never after an existing underscore.
                var isBoundary = i > 0
                    && name[i - 1] != '_'
                    && (!char.IsUpper(name[i - 1])
                        || (i + 1 < name.Length && char.IsLower(name[i + 1])));

                if (isBoundary)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLower(current, CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
