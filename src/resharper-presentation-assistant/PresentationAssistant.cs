﻿using System;
using JetBrains.ActionManagement;
using JetBrains.Application;
using JetBrains.Application.Parts;
using JetBrains.DataFlow;
using JetBrains.UI.ActionsRevised.Handlers;
using JetBrains.UI.ActionsRevised.Loader;
using JetBrains.UI.ActionsRevised.Shortcuts;
using JetBrains.UI.PopupMenu.Impl;
using JetBrains.Util;

namespace JetBrains.ReSharper.Plugins.PresentationAssistant
{
    [ShellComponent(Requirement = InstantiationRequirement.Instant)]
    public class PresentationAssistant
    {
        private static readonly TimeSpan MultiplierTimeout = TimeSpan.FromSeconds(10);

        private readonly PresentationAssistantWindowOwner presentationAssistantWindowOwner;
        private readonly IActionShortcuts actionShortcuts;
        private readonly VsShortcutFinder vsShortcutFinder;

        private DateTime lastDisplayed;
        private string lastActionId;
        private int multiplier;

        public PresentationAssistant(Lifetime lifetime, ActionEvents actionEvents,
                                     PresentationAssistantWindowOwner presentationAssistantWindowOwner,
                                     IActionShortcuts actionShortcuts, IActionDefs defs,
                                     VsShortcutFinder vsShortcutFinder)
        {
            this.presentationAssistantWindowOwner = presentationAssistantWindowOwner;
            this.actionShortcuts = actionShortcuts;
            this.vsShortcutFinder = vsShortcutFinder;
            actionEvents.AdviseExecuteAction(lifetime, OnAction);
        }

        private void OnAction(ActionEvents.ActionEventArgs obj)
        {
            // Would be nice to use IActionShortcuts here, but that only returns for a single scheme

            if (ActionIdBlacklist.IsBlacklisted(obj.ActionDef.ActionId))
                return;

            // TODO: Remove trailing ellipsis
            var text = MnemonicStore.RemoveMnemonicMark(obj.ActionDef.Text);
            text = string.IsNullOrEmpty(text) ? obj.ActionDef.ActionId : text;

            var now = DateTime.UtcNow;
            if (obj.ActionDef.ActionId != lastActionId)
                multiplier = 1;
            else if (now - lastDisplayed < MultiplierTimeout)
                multiplier++;

            var vsShortcut = GetPrimaryShortcutSequence(obj.ActionDef.VsShortcuts);
            // TODO: Make this a setting? Only show secondary scheme if different?
            ShortcutSequence intellijShortcut = null;
            if (!HasSamePrimaryShortcuts(obj.ActionDef))
                intellijShortcut = GetPrimaryShortcutSequence(obj.ActionDef.IdeaShortcuts);

            // There's no primary shortcut, try and find it by asking Visual Studio for the
            // shortcut of the overridden VS command (if there is one). Find any associated
            // intelliJ shortcut
            if (vsShortcut == null)
                vsShortcut = GetWellKnownShortcutSequence(obj.ActionDef, out intellijShortcut);

            var shortcut = new Shortcut
            {
                ActionId = obj.ActionDef.ActionId,
                Text = text,
                Description = obj.ActionDef.Description,
                VsShortcut = vsShortcut,
                IntellijShortcut = intellijShortcut,
                CurrentScheme = actionShortcuts.CurrentScheme,
                Multiplier = multiplier
            };

            presentationAssistantWindowOwner.Show(shortcut);

            lastDisplayed = now;
            lastActionId = obj.ActionDef.ActionId;
        }

        private static bool HasSamePrimaryShortcuts(IActionDefWithId actionDef)
        {
            var vsShortcuts = actionDef.VsShortcuts ?? EmptyArray<string>.Instance;
            var ideaShortcuts = actionDef.IdeaShortcuts ?? EmptyArray<string>.Instance;
            if (vsShortcuts.Length > 0 && ideaShortcuts.Length > 0)
                return vsShortcuts[0] == ideaShortcuts[0];

            return false;
        }

        private ShortcutSequence GetWellKnownShortcutSequence(IActionDefWithId actionDef,
            out ShortcutSequence intellijShortcut)
        {
            intellijShortcut = null;

            var s = vsShortcutFinder.GetOverriddenShortcut(actionDef);
            if (s != null)
                return GetShortcutSequence(s);

            // The escape action doesn't override any VS command, but also doesn't have a shortcut associated
            if (actionDef.ActionId == "Escape")
                return GetShortcutSequence("Escape");

            return null;
        }

        private static ShortcutSequence GetPrimaryShortcutSequence(string[] shortcuts)
        {
            if (shortcuts == null || shortcuts.Length == 0)
                return null;

            return GetShortcutSequence(shortcuts[0]);
        }

        private static ShortcutSequence GetShortcutSequence(string shortcut)
        {
            // ReSharper registers chords twice, once where the second char doesn't have modifiers
            // and once where the second char repeats the modifier of the first char.
            // E.g. Ctrl+R, R and Ctrl+R, Ctrl+R. This allows for flexibility in hitting that chord
            // (do you hold Ctrl down all the time, or just for the first char? Doesn't matter!)
            // Empirically, there are only two actions that have a genuine alternative shortcut -
            // SafeDelete (VS): Ctrl+R, D and Alt+Delete
            // Rename (IntelliJ): F2 and Shift+F6
            // These can be safely ignored, meaning we can just show the primary shortcut
            var parsedShortcut = ShortcutUtil.ParseKeyboardShortcut(shortcut);
            if (parsedShortcut == null)
                return null;

            return GetShortcutSequence(parsedShortcut);
        }

        private static ShortcutSequence GetShortcutSequence(ActionShortcut parsedShortcut)
        {
            var details = new ShortcutDetails[parsedShortcut.KeyboardShortcuts.Length];
            for (int i = 0; i < parsedShortcut.KeyboardShortcuts.Length; i++)
            {
                var keyboardShortcut = parsedShortcut.KeyboardShortcuts[i];
                details[i] = new ShortcutDetails(KeyConverter.Convert(keyboardShortcut.Key),
                    keyboardShortcut.Modifiers);
            }
            return new ShortcutSequence(details);
        }
    }
}
