﻿// Copyright (c) Matt Lacey Ltd. All rights reserved.
// Licensed under the MIT license.

using RapidXamlToolkit.Resources;
using RapidXamlToolkit.XamlAnalysis.Actions;

namespace RapidXamlToolkit.XamlAnalysis.Tags
{
    public class SelectedItemBindingModeTag : RapidXamlDisplayedTag
    {
        public SelectedItemBindingModeTag(TagDependencies tagDeps)
            : base(tagDeps, "RXT160", TagErrorType.Warning)
        {
            this.SuggestedAction = typeof(SelectedItemBindingModeAction);
            this.ToolTip = StringRes.UI_XamlAnalysisSetBindingModeToTwoWayToolTip;
            this.Description = StringRes.UI_XamlAnalysisSetBindingModeToTwoWayDescription;
        }

        public int InsertPosition { get; set; }

        public string ExistingBindingMode { get; set; }
    }
}
