// -------------------------------------------------------------------------
// Copyright (c) 2026 Clarivex Technologies. All rights reserved.
// Pervaxis Platform - Genesis Edition
// -------------------------------------------------------------------------

namespace Pervaxis.Genesis.Base.Abstractions;

/// <summary>
/// Defines methods for loading and parsing template configuration files.
/// Supports JSON and YAML formats for AWS CloudFormation, Terraform, and other IaC templates.
/// </summary>
public interface ITemplateConfigurationLoader
{
    /// <summary>
    /// Loads a template configuration from a file path.
    /// </summary>
    /// <param name="filePath">The path to the template file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed template as a dictionary.</returns>
    Task<IDictionary<string, object>> LoadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a template configuration from a string content.
    /// </summary>
    /// <param name="content">The template content.</param>
    /// <param name="format">The format of the template (json or yaml).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed template as a dictionary.</returns>
    Task<IDictionary<string, object>> LoadFromStringAsync(
        string content,
        string format,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a template configuration from an embedded resource.
    /// </summary>
    /// <param name="resourceName">The fully qualified resource name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed template as a dictionary.</returns>
    Task<IDictionary<string, object>> LoadFromResourceAsync(
        string resourceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a template configuration against a schema.
    /// </summary>
    /// <param name="template">The template to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool ValidateTemplate(IDictionary<string, object> template);
}
