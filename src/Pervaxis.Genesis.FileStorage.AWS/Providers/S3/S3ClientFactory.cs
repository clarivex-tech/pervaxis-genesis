/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 *
 * NOTICE: All intellectual and technical concepts contained
 * herein are proprietary to Clarivex Technologies Private Limited
 * and may be covered by Indian and Foreign Patents,
 * patents in process, and are protected by trade secret or
 * copyright law. Dissemination of this information or reproduction
 * of this material is strictly forbidden unless prior written
 * permission is obtained from Clarivex Technologies Private Limited.
 *
 * Product:   Pervaxis Platform
 * Website:   https://clarivex.tech
 ************************************************************************
 */

using Amazon.S3;
using Pervaxis.Genesis.FileStorage.AWS.Options;

namespace Pervaxis.Genesis.FileStorage.AWS.Providers.S3;

/// <summary>
/// Creates the <see cref="IAmazonS3"/> client for the file storage module from <see cref="FileStorageOptions"/>.
/// </summary>
internal static class S3ClientFactory
{
    /// <summary>
    /// Creates a new S3 client for the configured region, honouring the local emulator settings.
    /// </summary>
    /// <param name="options">File storage options.</param>
    /// <returns>A new <see cref="IAmazonS3"/> client. The caller owns its lifetime.</returns>
    internal static IAmazonS3 Create(FileStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var config = new AmazonS3Config
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region)
        };

        if (options.UseLocalEmulator)
        {
            var emulatorUrl = options.LocalEmulatorUrl?.ToString() ?? "http://localhost:4566";
            config.ServiceURL = emulatorUrl;
            config.ForcePathStyle = true;
        }

        return new AmazonS3Client(config);
    }
}
