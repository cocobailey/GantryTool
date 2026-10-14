using System;
using ColossalFramework.PlatformServices;
using ColossalFramework.UI;
using GantryTool.Data;
using UnityEngine;

namespace GantryTool.UI
{
    /// <summary>
    /// "What's new" popup, shown once on the first city load after an update. Players who already used the mod
    /// (a settings file, or gantries in the city) see it; brand-new players don't. Never blocks loading.
    /// </summary>
    public sealed class UpdateNotesPanel : UIPanel
    {
        /// <summary>The feature release these notes describe; bump it with new notes.</summary>
        public const string NotesVersion = "1.1";

        private const string ChangeNotesUrl = "https://steamcommunity.com/sharedfiles/filedetails/changelog/3814479293";
        private const float PanelWidth = 460f;
        private const float Margin = 16f;

        private static readonly string[][] Notes =
        {
            new[] { "Edit gantries", "click one to move it, change its height and size, or delete it. Ctrl+Z undoes." },
            new[] { "Edit single signals", "move and resize any head, pick its signs face by face." },
            new[] { "Speed limits", "set a number or follow the road's own limit (works with TM:PE)." },
            new[] { "Warnings and restrictions", "roadworks, queue, accident, slippery road, fog, no overtaking and more." },
            new[] { "Regional styles", "Europe, Netherlands, UK, United States, Canada and Australia/NZ." },
            new[] { "Custom signs", "your own text or pictures." }
        };

        /// <summary>Shows the notes if this player hasn't seen them yet. Call after the city has loaded.</summary>
        public static void ShowIfNew()
        {
            try
            {
                Settings s = Settings.Current;
                if (s.LastSeenNotes == NotesVersion)
                {
                    return;
                }

                if (!Settings.FileExisted && GantryManager.Count == 0)
                {
                    return; // looks like a new player: nothing "new" to tell them yet (asked again next load)
                }

                UIView.GetAView().AddUIComponent(typeof(UpdateNotesPanel));
                Log.Info("Showing what's new in " + NotesVersion);
            }
            catch (Exception e)
            {
                Log.Error("What's new popup failed", e);
            }
        }

        public override void Awake()
        {
            base.Awake();
            name = "GantryToolWhatsNew";
            backgroundSprite = "MenuPanel2";
            width = PanelWidth;

            var title = UIUtils.CreateLabel(this, "Gantry Tool " + NotesVersion, 1.1f);
            title.relativePosition = new Vector3(Margin, 12f);

            var drag = AddUIComponent<UIDragHandle>();
            drag.size = new Vector2(PanelWidth, 40f);
            drag.relativePosition = Vector3.zero;
            drag.target = this;

            float y = 52f;
            foreach (string[] note in Notes)
            {
                var label = UIUtils.CreateLabel(this, "", 0.8f);
                label.processMarkup = true;
                label.autoSize = false;
                label.wordWrap = true;
                label.width = PanelWidth - 2f * Margin;
                label.autoHeight = true;
                label.text = "<color #ffcc66>" + note[0] + ":</color> " + note[1];
                label.relativePosition = new Vector3(Margin, y);
                y += Mathf.Max(label.height, 18f) + 8f;
            }

            y += 8f;
            float buttonWidth = (PanelWidth - 2f * Margin - 8f) / 2f;
            var notes = CreateButton("Full change notes", buttonWidth, OpenChangeNotes);
            notes.relativePosition = new Vector3(Margin, y);
            var ok = CreateButton("Got it", buttonWidth, Close);
            ok.relativePosition = new Vector3(Margin + buttonWidth + 8f, y);

            height = y + 28f + Margin;
        }

        public override void Start()
        {
            base.Start();
            UIView view = GetUIView();
            relativePosition = new Vector3(Mathf.Round((view.fixedWidth - width) / 2f), Mathf.Round((view.fixedHeight - height) / 2f));
            BringToFront();
        }

        private UIButton CreateButton(string text, float w, Action onClick)
        {
            var b = AddUIComponent<UIButton>();
            b.text = text;
            b.textScale = 0.85f;
            b.normalBgSprite = "ButtonMenu";
            b.hoveredBgSprite = "ButtonMenuHovered";
            b.pressedBgSprite = "ButtonMenuPressed";
            b.size = new Vector2(w, 32f);
            b.eventClick += (c, p) => onClick();
            return b;
        }

        private void OpenChangeNotes()
        {
            try
            {
                if (PlatformService.platformType == PlatformType.Steam && PlatformService.IsOverlayEnabled())
                {
                    PlatformService.ActivateGameOverlayToWebPage(ChangeNotesUrl);
                }
                else
                {
                    Application.OpenURL(ChangeNotesUrl);
                }
            }
            catch (Exception e)
            {
                Log.Error("Opening the change notes failed", e);
            }

            Close();
        }

        private void Close()
        {
            try
            {
                Settings.Current.LastSeenNotes = NotesVersion;
                Settings.Current.Save();
            }
            catch (Exception e)
            {
                Log.Error("Saving what's new state failed", e);
            }

            Destroy(gameObject);
        }
    }
}
