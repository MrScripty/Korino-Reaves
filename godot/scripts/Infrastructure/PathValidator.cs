using System;
using System.Collections.Generic;
using System.IO;

namespace UAssetViewer.Infrastructure;

/// <summary>
/// Centralized path normalization and root-containment checks for external input.
/// </summary>
public static class PathValidator
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static IReadOnlyList<string> GetDefaultFilesystemRoots(params string?[] additionalRoots)
    {
        var roots = new HashSet<string>(PathComparer);

        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                {
                    AddRoot(roots, drive.RootDirectory.FullName);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        foreach (var additionalRoot in additionalRoots)
        {
            AddRoot(roots, additionalRoot);
        }

        return new List<string>(roots);
    }

    public static bool TryResolveWithinRoot(
        string? rawPath,
        string allowedRoot,
        out string validatedPath,
        out string error,
        bool requireExists = true,
        bool allowFiles = true,
        bool allowDirectories = true)
    {
        return TryResolveWithinRoots(
            rawPath,
            new[] { allowedRoot },
            out validatedPath,
            out error,
            requireExists,
            allowFiles,
            allowDirectories);
    }

    public static bool TryResolveWithinRoots(
        string? rawPath,
        IEnumerable<string> allowedRoots,
        out string validatedPath,
        out string error,
        bool requireExists = true,
        bool allowFiles = true,
        bool allowDirectories = true)
    {
        validatedPath = string.Empty;

        if (!InputValidator.TryValidateRequired(rawPath, "path", out var normalizedInput, out error))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(normalizedInput);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid path: {ex.Message}";
            return false;
        }

        var fileExists = File.Exists(fullPath);
        var directoryExists = Directory.Exists(fullPath);

        if (requireExists && !fileExists && !directoryExists)
        {
            error = $"Path not found: {fullPath}";
            return false;
        }

        if (fileExists && !allowFiles)
        {
            error = $"Expected a directory path: {fullPath}";
            return false;
        }

        if (directoryExists && !allowDirectories)
        {
            error = $"Expected a file path: {fullPath}";
            return false;
        }

        if (fileExists || directoryExists)
        {
            validatedPath = GetCanonicalExistingPath(fullPath, directoryExists);
        }
        else
        {
            var parentDirectory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
            {
                error = $"Parent directory not found: {parentDirectory ?? fullPath}";
                return false;
            }

            var canonicalParent = GetCanonicalExistingPath(parentDirectory, isDirectory: true);
            validatedPath = Path.Combine(canonicalParent, Path.GetFileName(fullPath));
        }

        if (!IsWithinAllowedRoots(validatedPath, allowedRoots))
        {
            error = $"Path is outside the allowed roots: {validatedPath}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static void AddRoot(ISet<string> roots, string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        try
        {
            var fullRoot = Path.GetFullPath(root);
            if (Directory.Exists(fullRoot))
            {
                roots.Add(GetCanonicalExistingPath(fullRoot, isDirectory: true));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }

    private static bool IsWithinAllowedRoots(string candidatePath, IEnumerable<string> allowedRoots)
    {
        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var normalizedRoot = Directory.Exists(root)
                ? GetCanonicalExistingPath(root, isDirectory: true)
                : Path.GetFullPath(root);

            if (IsWithinRoot(candidatePath, normalizedRoot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWithinRoot(string candidatePath, string allowedRoot)
    {
        var normalizedCandidate = NormalizePathForComparison(candidatePath);
        var normalizedRoot = NormalizePathForComparison(allowedRoot);

        if (string.Equals(normalizedCandidate, normalizedRoot, PathComparison))
        {
            return true;
        }

        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(rootWithSeparator, PathComparison);
    }

    private static string GetCanonicalExistingPath(string path, bool isDirectory)
    {
        FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        var resolvedTarget = info.ResolveLinkTarget(returnFinalTarget: true);
        var canonical = resolvedTarget?.FullName ?? info.FullName;
        return NormalizePathForComparison(canonical);
    }

    private static string NormalizePathForComparison(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed)
            ? Path.GetPathRoot(path) ?? path
            : trimmed;
    }
}
