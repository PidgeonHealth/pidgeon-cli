// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Pidgeon.CLI.Commands;
using Pidgeon.CLI.Services;
using PathOrch = Pidgeon.CLI.Commands.Paths.Orchestrators;
using SessionOrch = Pidgeon.CLI.Commands.Session.Orchestrators;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Application.Interfaces.Search;
using Pidgeon.Core.Application.Services.Configuration;
using Pidgeon.Core.Application.Services.Generation;
using Pidgeon.Core.Application.Services.Search;
using Pidgeon.Core.Extensions;
using Pidgeon.Data.Baseline;
using System.Reflection;

namespace Pidgeon.CLI.Extensions;

/// <summary>
/// Composition root for the free-core public CLI build (<see cref="PublicProgram"/>). Registers
/// the community Core generation/validation graph over the Baseline data package, the community
/// command catalog, and their supporting CLI-local services — deliberately independent of
/// <see cref="ServiceCollectionExtensions.AddCliCommands"/>/<c>AddCliServices</c>, which pull in
/// Flock/Loft/Migrate orchestrators and commercial-only services (telemetry, Mirth/Rhapsody/MLLP
/// clients, recipe-backed shared config) that do not exist in this composition.
/// </summary>
public static class CommunityServiceCollectionExtensions
{
    public static IServiceCollection AddPidgeonCliCommunity(this IServiceCollection services)
    {
        // Core generation + validation over the Baseline data package (config/public-compositions/community.yaml).
        services.AddPidgeonBaselineData();
        services.AddPidgeonHl7Generation();
        services.AddPidgeonFhirGeneration();
        services.AddPidgeonNcpdpGeneration();
        services.AddPidgeonHl7Validation();
        services.AddPidgeonFhirValidation();
        services.AddPidgeonFhirIgPackages();
        services.AddPidgeonCommunityProfileValidation();
        services.AddPidgeonCommunityDeIdentification();
        services.AddPidgeonCommunityDataPackages();
        services.AddPidgeonCommunityCapabilitySignaling();
        services.AddScoped<IConfigurationService, ConfigurationService>();
        services.AddScoped<IFieldDiscoveryService, FieldDiscoveryService>();
        services.AddScoped<IMessageSearchService, MessageSearchService>();
        services.AddScoped<IManifestReplayService, ManifestReplayService>();
        services.AddScoped<IShareProfileValidator, ShareProfileValidator>();
        services.AddScoped<IFieldPathResolver, FieldPathResolverService>();
        services.AddScoped<IStandardFieldPathPlugin, Pidgeon.Core.Infrastructure.Configuration.HL7FieldPathPlugin>();
        services.AddScoped<IStandardFieldPathPlugin, Pidgeon.Core.Infrastructure.Configuration.FHIRFieldPathPlugin>();
        services.AddScoped<IValidationProfileResolver, CommunityVendorProfileResolver>();

        // Community command catalog: every CommandBuilderBase subtype compiled into this
        // assembly. Fail-closed by construction — a command not in the community manifest
        // does not exist in this binary and therefore cannot be discovered here.
        var assembly = Assembly.GetExecutingAssembly();
        var commandTypes = assembly.GetTypes()
            .Where(type => type.IsClass &&
                          !type.IsAbstract &&
                          type.IsSubclassOf(typeof(CommandBuilderBase)))
            .ToList();

        foreach (var commandType in commandTypes)
        {
            services.AddScoped(commandType);
        }

        services.AddPathCommandOrchestrators();
        services.AddSessionCommandOrchestrators();

        // CLI-local support services (Services/ classes ending in Service/Helper), the same
        // convention ServiceCollectionExtensions.AddCliServices uses, scoped to this assembly's
        // actual contents so nothing commercial-only can be pulled in accidentally.
        var serviceTypes = assembly.GetTypes()
            .Where(type => type.IsClass &&
                          !type.IsAbstract &&
                          type.Namespace == "Pidgeon.CLI.Services" &&
                          type != typeof(ConfigurationStorage) &&
                          (type.Name.EndsWith("Service") || type.Name.EndsWith("Helper") || type.Name.EndsWith("Storage")))
            .ToList();

        foreach (var serviceType in serviceTypes)
        {
            services.AddScoped(serviceType);
        }

        return services;
    }

    private static IServiceCollection AddPathCommandOrchestrators(this IServiceCollection services)
    {
        services.AddScoped<PathOrch.ListPathOrchestrator>();
        services.AddScoped<PathOrch.ResolvePathOrchestrator>();
        services.AddScoped<PathOrch.ValidatePathOrchestrator>();
        services.AddScoped<PathOrch.SearchPathOrchestrator>();
        return services;
    }

    private static IServiceCollection AddSessionCommandOrchestrators(this IServiceCollection services)
    {
        services.AddScoped<SessionOrch.StatusSessionOrchestrator>();
        services.AddScoped<SessionOrch.SaveSessionOrchestrator>();
        services.AddScoped<SessionOrch.CreateSessionOrchestrator>();
        services.AddScoped<SessionOrch.UseSessionOrchestrator>();
        services.AddScoped<SessionOrch.ListSessionsOrchestrator>();
        services.AddScoped<SessionOrch.ShowSessionOrchestrator>();
        services.AddScoped<SessionOrch.ClearSessionOrchestrator>();
        services.AddScoped<SessionOrch.RemoveSessionOrchestrator>();
        services.AddScoped<SessionOrch.ExportSessionOrchestrator>();
        services.AddScoped<SessionOrch.ImportSessionOrchestrator>();
        return services;
    }
}
