using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal sealed class MenuIconLinkDialog : IconComparisonDialog<MenuIconCandidate>
    {
        protected override string ElementPrefix => "menu-icon";
        protected override string ListName => "menu-icon-candidates";
        protected override string TitleKey => "titles.LinkMenuIcons";
        protected override string MessageKey => "messages.ConfirmLinkMenuIcons";
        protected override bool ElideNameAtStart => true;

        internal static IReadOnlyList<MenuIconCandidate> Confirm(
            IReadOnlyList<MenuIconCandidate> candidates,
            IconGeneratorLocalization localization
        ) => ShowDialog<MenuIconLinkDialog>(candidates, localization).SelectedItems;

        protected override bool IsInitiallySelected(MenuIconCandidate item) => item.Selected;

        protected override Texture CurrentIcon(MenuIconCandidate item) => item.CurrentIcon;

        protected override Texture NewIcon(MenuIconCandidate item) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>(item.IconPath);

        protected override string Name(MenuIconCandidate item) =>
            item.MenuItem != null ? SelectionReference.HierarchyPath(item.MenuItem.transform) : "";

        protected override string Detail(MenuIconCandidate item) =>
            Localization.Text(
                $"options.MenuIconRelation{item.Relation}",
                item.Target != null ? item.Target.name : "",
                Path.GetFileName(item.IconPath)
            );

        protected override IEnumerable<string> SearchTexts(MenuIconCandidate item)
        {
            yield return item.MenuItem != null ? item.MenuItem.name : null;
            yield return item.Target != null ? item.Target.name : null;
            yield return Path.GetFileName(item.IconPath);
        }
    }
}
