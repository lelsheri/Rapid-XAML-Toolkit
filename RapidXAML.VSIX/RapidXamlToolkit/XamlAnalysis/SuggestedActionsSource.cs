﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using RapidXamlToolkit.XamlAnalysis.Actions;
using RapidXamlToolkit.XamlAnalysis.Tags;

namespace RapidXamlToolkit.XamlAnalysis
{
    public class SuggestedActionsSource : ISuggestedActionsSource, ISuggestedActionsSource2
    {
        private readonly ITextView _view;
        private string _file;
        private IViewTagAggregatorFactoryService _tagService;
        private readonly ISuggestedActionCategoryRegistryService _suggestedActionCategoryRegistry;

        public SuggestedActionsSource(IViewTagAggregatorFactoryService tagService, ISuggestedActionCategoryRegistryService suggestedActionCategoryRegistry, ITextView view, ITextBuffer textBuffer, string file)
        {
            this._tagService = tagService;
            this._suggestedActionCategoryRegistry = suggestedActionCategoryRegistry;
            this._view = view;
            this._file = file;

            // Don't want every change event as that is a lot during editing. Wait for a second of inactivity before reparsing.
            this.WhenViewLayoutChanged.Throttle(TimeSpan.FromSeconds(1)).Subscribe(e => this.OnViewLayoutChanged(this, e));

            RapidXamlDocumentCache.Add(this._file, textBuffer.CurrentSnapshot);
        }

        public event EventHandler<EventArgs> SuggestedActionsChanged
        {
            add { }
            remove { }
        }

        // Observable event wrapper
        public IObservable<TextViewLayoutChangedEventArgs> WhenViewLayoutChanged
        {
            get
            {
                return Observable
                    .FromEventPattern<EventHandler<TextViewLayoutChangedEventArgs>, TextViewLayoutChangedEventArgs>(
                        h => this._view.LayoutChanged += h,
                        h => this._view.LayoutChanged -= h)
                    .Select(x => x.EventArgs);
            }
        }

        public Task<ISuggestedActionCategorySet> GetSuggestedActionCategoriesAsync(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(
                () =>
                {
                    // Setting the only category to be "REFACTORING" causes the screwdriver icon to be shown, otherwise get the light bulb.
                    return this._suggestedActionCategoryRegistry.CreateSuggestedActionCategorySet(
                        this.GetTags(range).Any(t => t is RapidXamlErrorListTag) ? PredefinedSuggestedActionCategoryNames.Any
                                                                                 : PredefinedSuggestedActionCategoryNames.Refactoring);
                },
                cancellationToken,
                TaskCreationOptions.None,
                TaskScheduler.Current);
        }

        public Task<bool> HasSuggestedActionsAsync(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(() => { return this.GetTags(range).Any(); }, cancellationToken, TaskCreationOptions.None, TaskScheduler.Current);
        }

        public IEnumerable<SuggestedActionSet> GetSuggestedActions(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken)
        {
            var list = new List<SuggestedActionSet>();

            try
            {
                var rxTags = this.GetTags(range);

                foreach (var rxTag in rxTags)
                {
                    switch (rxTag.SuggestedAction.Name)
                    {
                        case nameof(InsertRowDefinitionAction):
                            list.AddRange(this.CreateActionSet(rxTag.Span, InsertRowDefinitionAction.Create((InsertRowDefinitionTag)rxTag, this._file, this._view)));
                            break;
                        case nameof(HardCodedStringAction):
                            list.AddRange(this.CreateActionSet(rxTag.Span, HardCodedStringAction.Create((HardCodedStringTag)rxTag, this._file, this._view)));
                            break;
                        case nameof(OtherHardCodedStringAction):
                            list.AddRange(this.CreateActionSet(rxTag.Span, OtherHardCodedStringAction.Create((OtherHardCodedStringTag)rxTag, this._file, this._view)));
                            break;
                        case nameof(AddRowDefinitionsAction):
                            list.AddRange(this.CreateActionSet(rxTag.Span, AddRowDefinitionsAction.Create((AddRowDefinitionsTag)rxTag)));
                            break;
                        case nameof(AddColumnDefinitionsAction):
                            list.AddRange(this.CreateActionSet(rxTag.Span, AddColumnDefinitionsAction.Create((AddColumnDefinitionsTag)rxTag)));
                            break;
                        case nameof(AddRowAndColumnDefinitionsAction):
                            list.AddRange(this.CreateActionSet(rxTag.Span, AddRowAndColumnDefinitionsAction.Create((AddRowAndColumnDefinitionsTag)rxTag)));
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                RapidXamlPackage.Logger?.RecordException(e);
            }

            return list;
        }

        public IEnumerable<SuggestedActionSet> CreateActionSet(Span span, params BaseSuggestedAction[] actions)
        {
            var enabledActions = actions.Where(action => action.IsEnabled);
            return new[]
            {
                new SuggestedActionSet(
                    PredefinedSuggestedActionCategoryNames.Refactoring,
                    actions: enabledActions,
                    title: "Rapid XAML",
                    priority: SuggestedActionSetPriority.None,
                    applicableToSpan: span),
            };
        }

        public void Dispose()
        {
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            // TODO: find out if we need a LightBulbTelemetryGuid and what value to use if we do
            telemetryId = Guid.Empty;
            return false;
        }

        private void OnViewLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            // Layout change can happen a lot, but only interested in if the text has changed.
            // It would be "nice" to only reparse the changed lines in large documents but would need to keep track or any processors that work on the encapsulated changes.
            // Caching processors for partial re-parsing would be complicated. Considering this a low priority optimization.
            if (e.OldSnapshot != e.NewSnapshot)
            {
                RapidXamlDocumentCache.Update(this._file, e.NewViewState.EditSnapshot);
            }
        }

        private IEnumerable<IRapidXamlTag> GetTags(SnapshotSpan span)
        {
            return RapidXamlDocumentCache.AdornmentTags(this._file).Where(t => t.Span.IntersectsWith(span)).Select(t => t);
        }

        private IEnumerable<IMappingTagSpan<IRapidXamlTag>> GetErrorTags(ITextView view, SnapshotSpan span)
        {
            return this._tagService.CreateTagAggregator<IRapidXamlTag>(view).GetTags(span);
        }
    }
}
