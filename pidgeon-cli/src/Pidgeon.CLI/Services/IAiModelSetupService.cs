// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.CLI.Services;

/// <summary>
/// Split out as an interface so <see cref="FirstTimeUserService"/> depends on the contract, not
/// the on-device-model-download implementation (<see cref="AiModelSetupService"/>, which needs
/// the runtime/model-download stack and is commercial-only); the community CLI build simply has
/// no registration for it, and <c>--init</c> treats an absent wizard as "unavailable in this
/// build" rather than failing to compile.
/// </summary>
public interface IAiModelSetupService
{
    /// <summary>Runs the interactive on-device AI model selection/download wizard.</summary>
    Task RunModelSelectionWizardAsync();
}
