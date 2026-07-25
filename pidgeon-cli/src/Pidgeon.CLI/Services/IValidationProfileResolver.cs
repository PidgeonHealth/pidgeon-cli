// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Configuration.Entities;

namespace Pidgeon.CLI.Services;

public enum ValidationProfileSource
{
    BuiltInDefaults,
    SpecRepository,
    SharedStore,
    LocalStore,
    SavedAddress
}

public sealed record ValidationProfileResolution
{
    public required string CanonicalName { get; init; }
    public required ValidationProfileSource Source { get; init; }
    public VendorSpecification? Specification { get; init; }
}

/// <summary>Composition-neutral vendor-profile seam used by the validation command.</summary>
public interface IValidationProfileResolver
{
    Task<ValidationProfileResolution?> ResolveAsync(string name);
}
