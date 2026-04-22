// -------------------------------------------------------------------------
// Copyright (c) 2026 Clarivex Technologies. All rights reserved.
// Pervaxis Platform - Genesis Edition
// -------------------------------------------------------------------------

using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pervaxis.Genesis.Base.Abstractions;
using Pervaxis.Genesis.Base.Exceptions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pervaxis.Genesis.Base.Configuration;

/// <summary>
/// Default implementation of template configuration loader supporting JSON and YAML formats.
/// </summary>
public sealed class TemplateConfigurationLoader : ITemplateConfigurationLoader
{
    private readonly ILogger<TemplateConfigurationLoader> _logger;
    private readonly IDeserializer _yamlDeserializer;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateConfigurationLoader"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public TemplateConfigurationLoader(ILogger<TemplateConfigurationLoader> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    /// <inheritdoc/>
    public async Task<IDictionary<string, object>> LoadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new GenesisConfigurationException(
                $"Template file not found: {filePath}");
        }

        _logger.LogInformation("Loading template from file: {FilePath}", filePath);

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var format = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();

        return await LoadFromStringAsync(content, format, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IDictionary<string, object>> LoadFromStringAsync(
        string content,
        string format,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        _logger.LogDebug("Parsing template content as {Format}", format);

        try
        {
            var template = format.ToLowerInvariant() switch
            {
                "json" => ParseJson(content),
                "yaml" or "yml" => ParseYaml(content),
                _ => throw new GenesisConfigurationException(
                    $"Unsupported template format: {format}. Supported formats: json, yaml, yml")
            };

            _logger.LogInformation("Successfully parsed template with {Count} root keys",
                template.Count);

            return Task.FromResult(template);
        }
        catch (Exception ex) when (ex is not GenesisConfigurationException)
        {
            throw new GenesisConfigurationException(
                $"Failed to parse template as {format}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IDictionary<string, object>> LoadFromResourceAsync(
        string resourceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        _logger.LogInformation("Loading template from embedded resource: {ResourceName}",
            resourceName);

        var assembly = Assembly.GetCallingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            throw new GenesisConfigurationException(
                $"Embedded resource not found: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken);

        var format = resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? "json"
            : "yaml";

        return await LoadFromStringAsync(content, format, cancellationToken);
    }

    /// <inheritdoc/>
    public bool ValidateTemplate(IDictionary<string, object> template)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (template.Count == 0)
        {
            _logger.LogWarning("Template validation failed: template is empty");
            return false;
        }

        _logger.LogDebug("Template validation passed");
        return true;
    }

    private IDictionary<string, object> ParseJson(string content)
    {
        var jsonDoc = JsonDocument.Parse(content, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        return JsonElementToDictionary(jsonDoc.RootElement);
    }

    private IDictionary<string, object> ParseYaml(string content)
    {
        var yamlObject = _yamlDeserializer.Deserialize<Dictionary<string, object>>(content);
        return yamlObject ?? new Dictionary<string, object>();
    }

    private static Dictionary<string, object> JsonElementToDictionary(JsonElement element)
    {
        var dictionary = new Dictionary<string, object>();

        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = JsonElementToObject(property.Value);
        }

        return dictionary;
    }

    private static object JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonElementToDictionary(element),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(JsonElementToObject)
                .ToArray(),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.ToString()
        };
    }
}
