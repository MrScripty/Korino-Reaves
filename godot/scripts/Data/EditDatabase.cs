// Edit Database - SQLite persistence for property edits
//
// Stores property edit records per-project so edits survive
// across sessions and can be reapplied when files are reopened.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Data;

public sealed record PropertyEdit(
    string FilePath,
    string PropertyPath,
    string? OriginalValue,
    string? EditedValue,
    string PropertyType,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed class EditDatabase : IDisposable
{
    private readonly IAppLogger _logger;
    private SqliteConnection? _connection;
    private bool _disposed;

    public bool IsOpen => _connection != null;

    public EditDatabase(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Open(string projectPath)
    {
        Close();

        var dbPath = GetDatabasePath(projectPath);
        var dbDir = Path.GetDirectoryName(dbPath);
        if (dbDir != null && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        EnsureSchema();
        _logger.Info("Edit database opened: {Path}", dbPath);
    }

    public void Close()
    {
        if (_connection != null)
        {
            _connection.Close();
            _connection.Dispose();
            _connection = null;
        }
    }

    public void SaveEdit(PropertyEdit edit)
    {
        if (_connection == null) return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO property_edits (file_path, property_path, original_value, edited_value, property_type, created_at, updated_at)
            VALUES ($filePath, $propertyPath, $originalValue, $editedValue, $propertyType, $createdAt, $updatedAt)
            ON CONFLICT(file_path, property_path) DO UPDATE SET
                edited_value = $editedValue,
                updated_at = $updatedAt";

        cmd.Parameters.AddWithValue("$filePath", edit.FilePath);
        cmd.Parameters.AddWithValue("$propertyPath", edit.PropertyPath);
        cmd.Parameters.AddWithValue("$originalValue", (object?)edit.OriginalValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$editedValue", (object?)edit.EditedValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$propertyType", edit.PropertyType);
        cmd.Parameters.AddWithValue("$createdAt", edit.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt", edit.UpdatedAt.ToString("o"));

        cmd.ExecuteNonQuery();
    }

    public void DeleteEdit(string filePath, string propertyPath)
    {
        if (_connection == null) return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM property_edits WHERE file_path = $filePath AND property_path = $propertyPath";
        cmd.Parameters.AddWithValue("$filePath", filePath);
        cmd.Parameters.AddWithValue("$propertyPath", propertyPath);
        cmd.ExecuteNonQuery();
    }

    public List<PropertyEdit> GetEditsForFile(string filePath)
    {
        var edits = new List<PropertyEdit>();
        if (_connection == null) return edits;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT file_path, property_path, original_value, edited_value, property_type, created_at, updated_at FROM property_edits WHERE file_path = $filePath";
        cmd.Parameters.AddWithValue("$filePath", filePath);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            edits.Add(ReadEdit(reader));
        }

        return edits;
    }

    public PropertyEdit? GetEdit(string filePath, string propertyPath)
    {
        if (_connection == null) return null;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT file_path, property_path, original_value, edited_value, property_type, created_at, updated_at FROM property_edits WHERE file_path = $filePath AND property_path = $propertyPath";
        cmd.Parameters.AddWithValue("$filePath", filePath);
        cmd.Parameters.AddWithValue("$propertyPath", propertyPath);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadEdit(reader) : null;
    }

    public HashSet<string> GetEditedFilePaths()
    {
        var paths = new HashSet<string>();
        if (_connection == null) return paths;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT file_path FROM property_edits";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
    }

    public void DeleteEditsForFile(string filePath)
    {
        if (_connection == null) return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM property_edits WHERE file_path = $filePath";
        cmd.Parameters.AddWithValue("$filePath", filePath);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Close();
            _disposed = true;
        }
    }

    private void EnsureSchema()
    {
        if (_connection == null) return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS property_edits (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL,
                property_path TEXT NOT NULL,
                original_value TEXT,
                edited_value TEXT,
                property_type TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                UNIQUE(file_path, property_path)
            );
            CREATE INDEX IF NOT EXISTS idx_edits_file_path ON property_edits(file_path)";

        cmd.ExecuteNonQuery();
    }

    private static PropertyEdit ReadEdit(SqliteDataReader reader)
    {
        return new PropertyEdit(
            FilePath: reader.GetString(0),
            PropertyPath: reader.GetString(1),
            OriginalValue: reader.IsDBNull(2) ? null : reader.GetString(2),
            EditedValue: reader.IsDBNull(3) ? null : reader.GetString(3),
            PropertyType: reader.GetString(4),
            CreatedAt: DateTime.Parse(reader.GetString(5)),
            UpdatedAt: DateTime.Parse(reader.GetString(6))
        );
    }

    private static string GetDatabasePath(string projectPath)
    {
        var projectRoot = projectPath;
        var dirName = Path.GetFileName(projectPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(dirName, "UE_data", StringComparison.OrdinalIgnoreCase))
        {
            projectRoot = Path.GetDirectoryName(projectPath) ?? projectPath;
        }

        return Path.Combine(projectRoot, "usr", "edits.db");
    }
}
