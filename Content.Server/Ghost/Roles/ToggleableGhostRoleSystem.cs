// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2023 Jark255
// SPDX-FileCopyrightText: 2023 Nemanja
// SPDX-FileCopyrightText: 2023 ShadowCommander
// SPDX-FileCopyrightText: 2023 deltanedas
// SPDX-FileCopyrightText: 2023 deltanedas <@deltanedas:kde.org>
// SPDX-FileCopyrightText: 2024 Kara
// SPDX-FileCopyrightText: 2024 Scribbles0
// SPDX-FileCopyrightText: 2024 TsjipTsjip
// SPDX-FileCopyrightText: 2024 poeMota
// SPDX-FileCopyrightText: 2025 Errant
// SPDX-FileCopyrightText: 2025 Ilya246
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Ghost.Roles.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction; // Goobstation
using Content.Shared.Interaction.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;

namespace Content.Server.Ghost.Roles;

/// <summary>
/// This handles logic and interaction related to <see cref="ToggleableGhostRoleComponent"/>
/// </summary>
public sealed class ToggleableGhostRoleSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<ToggleableGhostRoleComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ToggleableGhostRoleComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ToggleableGhostRoleComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<ToggleableGhostRoleComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<ToggleableGhostRoleComponent, GetVerbsEvent<ActivationVerb>>(AddWipeVerb);

        SubscribeLocalEvent<ToggleableGhostRoleComponent, ActivateInWorldEvent>(OnActivateInWorld); // Goobstation
    }

    private void OnUseInHand(EntityUid uid, ToggleableGhostRoleComponent component, UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        // Goobstation - intentional conflict landmine: if you see conflicts here, move the new code to TryActivate()
        TryActivate(uid, component, args.User);
    }

    // Goobstation
    private void OnActivateInWorld(EntityUid uid, ToggleableGhostRoleComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        TryActivate(uid, component, args.User);
    }

    private void OnExamined(EntityUid uid, ToggleableGhostRoleComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
        {
            args.PushMarkup(Loc.GetString(component.ExamineTextMindPresent));
        }
        else if (HasComp<GhostTakeoverAvailableComponent>(uid))
        {
            args.PushMarkup(Loc.GetString(component.ExamineTextMindSearching));
        }
        else
        {
            args.PushMarkup(Loc.GetString(component.ExamineTextNoMind));
        }
    }

    private void OnMindAdded(EntityUid uid, ToggleableGhostRoleComponent pai, MindAddedMessage args)
    {
        // Mind was added, shutdown the ghost role stuff so it won't get in the way
        RemCompDeferred<GhostTakeoverAvailableComponent>(uid);
        UpdateAppearance(uid, ToggleableGhostRoleStatus.On);
    }

    private void OnMindRemoved(EntityUid uid, ToggleableGhostRoleComponent component, MindRemovedMessage args)
    {
        // Mind was removed, prepare for re-toggle of the role
        RemCompDeferred<GhostRoleComponent>(uid);
        UpdateAppearance(uid, ToggleableGhostRoleStatus.Off);
    }

    private void UpdateAppearance(EntityUid uid, ToggleableGhostRoleStatus status)
    {
        _appearance.SetData(uid, ToggleableGhostRoleVisuals.Status, status);
    }

    private void AddWipeVerb(EntityUid uid, ToggleableGhostRoleComponent component, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanComplexInteract) // Goobstation - replace hands check with CanComplexInteract
            return;

        if (TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
        {
            ActivationVerb verb = new()
            {
                Text = Loc.GetString(component.WipeVerbText),
                Act = () =>
                {
                    if (!_mind.TryGetMind(uid, out var mindId, out var mind))
                        return;
                    // Wiping device :(
                    // The shutdown of the Mind should cause automatic reset of the pAI during OnMindRemoved
                    _mind.TransferTo(mindId, null, mind: mind);
                    _popup.PopupEntity(Loc.GetString(component.WipeVerbPopup), uid, args.User, PopupType.Large);
                }
            };
            args.Verbs.Add(verb);
        }
        else if (HasComp<GhostTakeoverAvailableComponent>(uid))
        {
            ActivationVerb verb = new()
            {
                Text = Loc.GetString(component.StopSearchVerbText),
                Act = () =>
                {
                    if (component.Deleted || !HasComp<GhostTakeoverAvailableComponent>(uid))
                        return;

                    RemCompDeferred<GhostTakeoverAvailableComponent>(uid);
                    RemCompDeferred<GhostRoleComponent>(uid);
                    _popup.PopupEntity(Loc.GetString(component.StopSearchVerbPopup), uid, args.User);
                    UpdateAppearance(uid, ToggleableGhostRoleStatus.Off);
                }
            };
            args.Verbs.Add(verb);
        }
    }

    // Goobstation
    public void TryActivate(EntityUid uid, ToggleableGhostRoleComponent component, EntityUid user)
    {
        // check if a mind is present
        if (TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
        {
            _popup.PopupEntity(Loc.GetString(component.ExamineTextMindPresent), uid, user, PopupType.Large);
            return;
        }
        if (HasComp<GhostTakeoverAvailableComponent>(uid))
        {
            _popup.PopupEntity(Loc.GetString(component.ExamineTextMindSearching), uid, user);
            return;
        }
        _popup.PopupEntity(Loc.GetString(component.BeginSearchingText), uid, user);

        UpdateAppearance(uid, ToggleableGhostRoleStatus.Searching);

        var ghostRole = EnsureComp<GhostRoleComponent>(uid);
        EnsureComp<GhostTakeoverAvailableComponent>(uid);

        //GhostRoleComponent inherits custom settings from the ToggleableGhostRoleComponent
        ghostRole.RoleName = Loc.GetString(component.RoleName);
        ghostRole.RoleDescription = Loc.GetString(component.RoleDescription);
        ghostRole.RoleRules = Loc.GetString(component.RoleRules);
        ghostRole.JobProto = component.JobProto;
        ghostRole.MindRoles = component.MindRoles;
    }

    /// <summary>
    /// If there is a player present, kicks it out.
    /// If not, prevents future ghosts taking it.
    /// No popups are made, but appearance is updated.
    /// </summary>
    public void Wipe(EntityUid uid)
    {
        if (TryComp<MindContainerComponent>(uid, out var mindContainer) &&
            mindContainer.HasMind &&
            _mind.TryGetMind(uid, out var mindId, out var mind))
        {
            _mind.TransferTo(mindId, null, mind: mind);
        }

        if (!HasComp<GhostTakeoverAvailableComponent>(uid))
            return;

        RemCompDeferred<GhostTakeoverAvailableComponent>(uid);
        RemCompDeferred<GhostRoleComponent>(uid);
        UpdateAppearance(uid, ToggleableGhostRoleStatus.Off);
    }
}
