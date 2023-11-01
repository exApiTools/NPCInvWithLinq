﻿using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Cache;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using ItemFilterLibrary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using static NPCInvWithLinq.ServerAndStashWindow;

namespace NPCInvWithLinq
{
    public class ServerAndStashWindow
    {
        public IList<WindowSet> Tabs { get; set; }
        public class WindowSet
        {
            public int Index { get; set; }
            public string Title { get; set; }
            public bool IsVisible { get; set; }
            public List<CustomItemData> ServerItems { get; set; }
            public List<CustomItemData> TradeWindowItems { get; set; }
            public override string ToString()
            {
                return $"Tab({Title}) is Index({Index}) IsVisible({IsVisible}) [ServerItems({ServerItems.Count}), TradeWindowItems({TradeWindowItems.Count})]";
            }
        }
    }

    public class NPCInvWithLinq : BaseSettingsPlugin<NPCInvWithLinqSettings>
    {
        private readonly TimeCache<List<WindowSet>> _storedStashAndWindows;
        private List<ItemFilter> _itemFilters;
        private PurchaseWindow _purchaseWindowHideout;
        private PurchaseWindow _purchaseWindow;
        private IList<InventoryHolder> _npcInventories;

        public NPCInvWithLinq()
        {
            Name = "NPC Inv With Linq";
            _storedStashAndWindows = new TimeCache<List<WindowSet>>(UpdateCurrentTradeWindow, 50);
        }
        public override bool Initialise()
        {
            Settings.ReloadFilters.OnPressed = LoadRuleFiles;
            LoadRuleFiles();
            return true;
        }

        public override Job Tick()
        {
            _purchaseWindowHideout = GameController.Game.IngameState.IngameUi.PurchaseWindowHideout;
            _purchaseWindow = GameController.Game.IngameState.IngameUi.PurchaseWindow;
            _npcInventories = GameController.Game.IngameState.ServerData.NPCInventories;

            return null;
        }

        public override void Render()
        {
            Element _hoveredItem = null;

            if (GameController.IngameState.UIHover is { Address: not 0 } h && h.Entity.IsValid)
                _hoveredItem = GameController.IngameState.UIHover;

            if (!_purchaseWindowHideout.IsVisible && !_purchaseWindow.IsVisible)
                return;

            // Draw open inventory and then for non visible inventories add items to a list that are in teh filter and draw on the side in an imgui window
            List<string> unSeenItems = new List<string>();

            foreach (var storedTab in _storedStashAndWindows.Value)
            {
                if (storedTab.IsVisible)
                {
                    //Hand is visible part here (add to a list of items to draw?)
                    foreach (var visibleItem in storedTab.TradeWindowItems)
                    {
                        if (visibleItem == null) continue;
                        if (!ItemInFilter(visibleItem)) continue;

                        if (_hoveredItem != null && _hoveredItem.Tooltip.GetClientRectCache.Intersects(visibleItem.ClientRectangleCache) && _hoveredItem.Entity.Address != visibleItem.Entity.Address)
                        {
                            var dimmedColor = Settings.FrameColor.Value; dimmedColor.A = 45;
                            Graphics.DrawFrame(visibleItem.ClientRectangleCache, dimmedColor, Settings.FrameThickness);
                        }
                        else
                        {
                            Graphics.DrawFrame(visibleItem.ClientRectangleCache, Settings.FrameColor, Settings.FrameThickness);
                        }
                    }
                }
                else
                {
                    // not visible part here (add to a list of items to draw?)
                    var tabHadWantedItem = false;
                    foreach (var hiddenItem in storedTab.ServerItems)
                    {
                        if (hiddenItem == null) continue;
                        if (!ItemInFilter(hiddenItem)) continue;
                        if (!tabHadWantedItem)
                            unSeenItems.Add($"Tab [{storedTab.Title}]");

                        unSeenItems.Add($"\t{hiddenItem.Name}");

                        tabHadWantedItem = true;
                    }
                    if (tabHadWantedItem)
                        unSeenItems.Add($"");
                }
            }

            PurchaseWindow purchaseWindowItems = _purchaseWindowHideout.IsVisible ? _purchaseWindowHideout : _purchaseWindow;

            var startingPoint = purchaseWindowItems.TabContainer.GetClientRectCache.TopRight.ToVector2Num();
            startingPoint.X += 15;

            var longestText = unSeenItems.OrderByDescending(s => s.Length).FirstOrDefault();
            var textHeight = Graphics.MeasureText(longestText);
            var textPadding = 10;

            var serverItemsBox = new SharpDX.RectangleF
            {
                Height = textHeight.Y * unSeenItems.Count,
                Width = textHeight.X + (textPadding * 2),
                X = startingPoint.X,
                Y = startingPoint.Y
            };

            var boxColor = new SharpDX.Color(0, 0, 0, 150);
            var textColor = new SharpDX.Color(255, 255, 255, 230);

            if (_hoveredItem == null || !_hoveredItem.Tooltip.GetClientRectCache.Intersects(serverItemsBox))
            {
                Graphics.DrawBox(serverItemsBox, boxColor);

                for (int i = 0; i < unSeenItems.Count; i++)
                {
                    string stringItem = unSeenItems[i];
                    Graphics.DrawText(stringItem, new Vector2(startingPoint.X + textPadding, startingPoint.Y + (textHeight.Y * i)), textColor);
                }
            }



            if (Settings.FilterTest.Value is { Length: > 0 } && _hoveredItem != null)
            {
                var f = ItemFilter.LoadFromString(Settings.FilterTest);
                var matched = f.Matches(new ItemData(_hoveredItem.Entity, GameController.Files));
                DebugWindow.LogMsg($"Debug item match on hover: {matched}");
            }
        }

        public record FilterDirItem(string Name, string Path);

        public override void DrawSettings()
        {
            base.DrawSettings();

            if (ImGui.Button("Open Build Folder"))
                Process.Start("explorer.exe", ConfigDirectory);

            ImGui.Separator();

            ImGui.BulletText("Select Rules To Load");

            foreach (var rule in Settings.NPCInvRules)
            {
                var refToggle = rule.Enabled;
                if (ImGui.Checkbox(rule.Name, ref refToggle))
                {
                    rule.Enabled = refToggle;
                }
            }
        }

        private void PopulateFilterList(string pickitConfigFileDirectory, List<NPCInvRule> tempPickitRules, List<FilterDirItem> itemList)
        {
            foreach (var drItem in new DirectoryInfo(pickitConfigFileDirectory).GetFiles("*.ifl"))
            {
                itemList.Add(new FilterDirItem(drItem.Name, drItem.FullName));
            }

            if (itemList.Count > 0)
            {
                tempPickitRules.RemoveAll(rule => !itemList.Any(item => item.Name == rule.Name));

                foreach (var item in itemList)
                {
                    if (!tempPickitRules.Any(rule => rule.Name == item.Name))
                    {
                        tempPickitRules.Add(new NPCInvRule(item.Name, item.Path, false));
                    }
                }
            }
            Settings.NPCInvRules = tempPickitRules;
        }

        private void LoadRuleFiles()
        {
            var pickitConfigFileDirectory = ConfigDirectory;

            if (!Directory.Exists(pickitConfigFileDirectory))
            {
                Directory.CreateDirectory(pickitConfigFileDirectory);
                return;
            }

            List<ItemFilter> tempFilters = new List<ItemFilter>();
            var tempPickitRules = Settings.NPCInvRules;
            var itemList = new List<FilterDirItem>();
            PopulateFilterList(pickitConfigFileDirectory, tempPickitRules, itemList);

            tempPickitRules = Settings.NPCInvRules;
            if (Settings.NPCInvRules.Count > 0)
            {
                tempPickitRules.RemoveAll(rule => !itemList.Any(item => item.Name == rule.Name));

                foreach (var item in tempPickitRules)
                {
                    if (!item.Enabled)
                        continue;

                    if (File.Exists(item.Location))
                    {
                        tempFilters.Add(ItemFilter.LoadFromPath(item.Location));
                    }
                    else
                    {
                        LogError($"File '{item.Name}' not found.");
                    }
                }
            }
            _itemFilters = tempFilters;
        }

        private List<WindowSet> UpdateCurrentTradeWindow()
        {
            var newTabSet = new List<WindowSet>();

            if (_purchaseWindowHideout == null || _purchaseWindow == null)
                return newTabSet;

            PurchaseWindow purchaseWindowItems = null;
            WorldArea currentWorldArea = GameController.Game.IngameState.Data.CurrentWorldArea;

            if (currentWorldArea.IsHideout && _purchaseWindowHideout.IsVisible)
                purchaseWindowItems = _purchaseWindowHideout;
            else if (currentWorldArea.IsTown && _purchaseWindow.IsVisible)
                purchaseWindowItems = _purchaseWindow;

            if (purchaseWindowItems == null)
                return newTabSet;

            for (int i = 0; i < _npcInventories.Count; i++)
            {
                var newTab = new WindowSet
                {
                    Index = i,

                    ServerItems = _npcInventories[i].Inventory.Items
                        .ToList()
                        .Where(x => x?.Path != null)
                        .Select(x => new CustomItemData(x, GameController.Files))
                        .ToList(),

                    TradeWindowItems = purchaseWindowItems.TabContainer.AllInventories[i].VisibleInventoryItems
                        .ToList()
                        .Where(x => x.Item?.Path != null)
                        .Select(x => new CustomItemData(x.Item, GameController.Files, x.GetClientRectCache))
                        .ToList(),

                    Title = $"-{i + 1}-",

                    IsVisible = purchaseWindowItems.TabContainer.AllInventories[i].IsVisible
                };
                newTabSet.Add(newTab);
            }

            return newTabSet;
        }

        private bool ItemInFilter(ItemData item)
        {
            return _itemFilters?.Any(filter => filter.Matches(item)) ?? false;
        }
    }
}