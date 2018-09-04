﻿using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class InventorySlot
    {
        public Rectangle Rect;

        public Rectangle InteractRect;

        public bool Disabled;

        public GUIComponent.ComponentState State;
        
        public Vector2 DrawOffset;
        
        public Color Color;

        public Color BorderHighlightColor;
        private CoroutineHandle BorderHighlightCoroutine;
        
        public Sprite SlotSprite;

        public Keys QuickUseKey;

        public int SubInventoryDir = -1;
        
        public bool IsHighlighted
        {
            get
            {
                return State == GUIComponent.ComponentState.Hover;
            }
        }
        
        public GUIComponent.ComponentState EquipButtonState;
        public Rectangle EquipButtonRect
        {
            get
            {
                int buttonDir = Math.Sign(SubInventoryDir);

                Vector2 equipIndicatorPos = new Vector2(
                    Rect.Center.X - Inventory.EquipIndicator.size.X / 2 * Inventory.UIScale,
                    Rect.Center.Y + (Rect.Height / 2 + 20 * Inventory.UIScale) * buttonDir - Inventory.EquipIndicator.size.Y / 2 * Inventory.UIScale);
                equipIndicatorPos += DrawOffset;

                return new Rectangle(
                    (int)(equipIndicatorPos.X), (int)(equipIndicatorPos.Y),
                    (int)(Inventory.EquipIndicator.size.X * Inventory.UIScale), (int)(Inventory.EquipIndicator.size.Y * Inventory.UIScale));
            }
        }

        public InventorySlot(Rectangle rect)
        {
            Rect = rect;
            InteractRect = rect;
            InteractRect.Inflate(5, 5);
            State = GUIComponent.ComponentState.None;
            Color = Color.White * 0.4f;
        }

        public bool MouseOn()
        {
            Rectangle rect = InteractRect;
            rect.Location += DrawOffset.ToPoint();
            return rect.Contains(PlayerInput.MousePosition);
        }

        public void ShowBorderHighlight(Color color, float fadeInDuration, float fadeOutDuration)
        {
            if (BorderHighlightCoroutine != null)
            {
                CoroutineManager.StopCoroutines(BorderHighlightCoroutine);
                BorderHighlightCoroutine = null;
            }

            BorderHighlightCoroutine = CoroutineManager.StartCoroutine(UpdateBorderHighlight(color, fadeInDuration, fadeOutDuration));
        }

        private IEnumerable<object> UpdateBorderHighlight(Color color, float fadeInDuration, float fadeOutDuration)
        {
            float t = 0.0f;
            while (t < fadeInDuration + fadeOutDuration)
            {
                BorderHighlightColor = (t < fadeInDuration) ?
                    Color.Lerp(Color.Transparent, color, t / fadeInDuration) :
                    Color.Lerp(color, Color.Transparent, (t - fadeInDuration) / fadeOutDuration);

                t += CoroutineManager.DeltaTime;

                yield return CoroutineStatus.Running;
            }

            BorderHighlightColor = Color.Transparent;

            yield return CoroutineStatus.Success;
        }
    }

    partial class Inventory
    {
        public static float UIScale
        {
            get { return (GameMain.GraphicsWidth / 1920.0f + GameMain.GraphicsHeight / 1080.0f) / 2.0f * GameSettings.InventoryScale; }
        }

        public static int ContainedIndicatorHeight
        {
            get { return (int)(15 * GameSettings.InventoryScale); }
        }

        protected float prevUIScale = UIScale;
                
        protected static Sprite slotSpriteSmall, slotSpriteHorizontal, slotSpriteVertical, slotSpriteRound;
        public static Sprite EquipIndicator, EquipIndicatorOn;

        protected Point screenResolution;

        public float HideTimer;

        private bool isSubInventory;

        public class SlotReference
        {
            public readonly Inventory ParentInventory;
            public readonly InventorySlot Slot;
            public readonly int SlotIndex;

            public Inventory Inventory;

            public bool IsSubSlot;

            public SlotReference(Inventory parentInventory, InventorySlot slot, int slotIndex, bool isSubSlot, Inventory subInventory = null)
            {
                ParentInventory = parentInventory;
                Slot = slot;
                SlotIndex = slotIndex;
                Inventory = subInventory;
                IsSubSlot = isSubSlot;
            }
        }

        public static InventorySlot draggingSlot;
        public static Item draggingItem;

        public static Item doubleClickedItem;

        private int slotsPerRow;
        public int SlotsPerRow
        {
            set { slotsPerRow = Math.Max(1, value); }
        }

        protected static HashSet<SlotReference> highlightedSubInventorySlots = new HashSet<SlotReference>();
        //protected static List<Inventory> highlightedSubInventories = new List<Inventory>();

        protected static SlotReference selectedSlot;

        public InventorySlot[] slots;
        
        public Vector2 CenterPos
        {
            get;
            set;
        }
        
        public static SlotReference SelectedSlot
        {
            get { return selectedSlot; }
        }
        
        protected virtual void CreateSlots()
        {
            slots = new InventorySlot[capacity];

            int rectWidth = (int)(60 * UIScale), rectHeight = (int)(60 * UIScale);
            int spacingX = (int)(10 * UIScale);
            int spacingY = (int)((10 + EquipIndicator.size.Y) * UIScale);

            int rows = (int)Math.Ceiling((double)capacity / slotsPerRow);
            int columns = Math.Min(slotsPerRow, capacity);

            int startX = (int)(CenterPos.X * GameMain.GraphicsWidth) - (rectWidth * columns + spacingX * (columns - 1)) / 2;
            int startY = (int)(CenterPos.Y * GameMain.GraphicsHeight) - (rows * (spacingY + rectHeight)) / 2;

            Rectangle slotRect = new Rectangle(startX, startY, rectWidth, rectHeight);
            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = startX + (rectWidth + spacingX) * (i % slotsPerRow);
                slotRect.Y = startY + (rectHeight + spacingY) * ((int)Math.Floor((double)i / slotsPerRow));

                slots[i] = new InventorySlot(slotRect);
            }

            if (selectedSlot != null && selectedSlot.ParentInventory == this)
            {
                selectedSlot = new SlotReference(this, slots[selectedSlot.SlotIndex], selectedSlot.SlotIndex, selectedSlot.IsSubSlot, selectedSlot.Inventory);
            }

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        protected virtual bool HideSlot(int i)
        {
            return slots[i].Disabled || (hideEmptySlot[i] && Items[i] == null);
        }

        public virtual void Update(float deltaTime, Camera cam, bool subInventory = false)
        {
            syncItemsDelay = Math.Max(syncItemsDelay - deltaTime, 0.0f);

            if (slots == null || isSubInventory != subInventory)
            {
                CreateSlots();
                isSubInventory = subInventory;
            }

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) continue;
                UpdateSlot(slots[i], i, Items[i], subInventory);
            }
        }

        protected void UpdateSlot(InventorySlot slot, int slotIndex, Item item, bool isSubSlot)
        {
            Rectangle interactRect = slot.InteractRect;
            interactRect.Location += slot.DrawOffset.ToPoint();
            bool mouseOn = interactRect.Contains(PlayerInput.MousePosition) && !Locked && GUI.MouseOn == null;

            if (selectedSlot != null && selectedSlot.Slot != slot)
            {
                //subinventory slot highlighted -> don't allow highlighting this one
                if (selectedSlot.IsSubSlot && !isSubSlot)
                {
                    mouseOn = false;
                }
                else if (!selectedSlot.IsSubSlot && isSubSlot && mouseOn)
                {
                    selectedSlot = null;
                }
            }

            
            slot.State = GUIComponent.ComponentState.None;
            
            if (mouseOn && (draggingItem != null || selectedSlot == null || selectedSlot.Slot == slot))  
                // &&
                //(highlightedSubInventories.Count == 0 || highlightedSubInventories.Contains(this) || highlightedSubInventorySlot?.Slot == slot || highlightedSubInventory.Owner == item))
            {
                slot.State = GUIComponent.ComponentState.Hover;

                if (selectedSlot == null || (!selectedSlot.IsSubSlot && isSubSlot))
                {
                    selectedSlot = new SlotReference(this, slot, slotIndex, isSubSlot, Items[slotIndex]?.GetComponent<ItemContainer>()?.Inventory);
                }

                if (draggingItem == null)
                {
                    if (PlayerInput.LeftButtonHeld())
                    {
                        draggingItem = Items[slotIndex];
                        draggingSlot = slot;
                    }
                }
                else if (PlayerInput.LeftButtonReleased())
                {
                    if (PlayerInput.DoubleClicked())
                    {
                        doubleClickedItem = item;
                    }
                }               
            }
        }

        protected Inventory GetSubInventory(int slotIndex)
        {
            var item = Items[slotIndex];
            if (item == null) return null;

            var container = item.GetComponent<ItemContainer>();
            if (container == null) return null;

            return container.Inventory;
        }

        float openState;

        public void UpdateSubInventory(float deltaTime, int slotIndex, Camera cam)
        {
            var item = Items[slotIndex];
            if (item == null) return;

            var container = item.GetComponent<ItemContainer>();
            if (container == null || !container.DrawInventory) return;

            var subInventory = container.Inventory;            
            if (subInventory.slots == null) subInventory.CreateSlots();

            int itemCapacity = subInventory.Items.Length;
            var slot = slots[slotIndex];
            int dir = slot.SubInventoryDir;
            if (itemCapacity == 1 && false)
            {
                Point slotSize = (slotSpriteRound.size * UIScale).ToPoint();
                subInventory.slots[0].Rect = 
                    new Rectangle(slot.Rect.Center.X - slotSize.X / 2, dir > 0 ? slot.Rect.Bottom + 5 : slot.EquipButtonRect.Bottom + 5, slotSize.X, slotSize.Y);

                subInventory.slots[0].InteractRect = subInventory.slots[0].Rect;
                subInventory.slots[0].DrawOffset = slot.DrawOffset;
            }
            else
            {
                Rectangle subRect = slot.Rect;
                subRect.Width = slots[slotIndex].SlotSprite == null ? (int)(60 * UIScale) : (int)(slots[slotIndex].SlotSprite.size.X * UIScale);
                subRect.Height = (int)(60 * UIScale);

                int spacing = (int)(10 * UIScale);

                int columns = (int)Math.Max(Math.Floor(Math.Sqrt(itemCapacity)), 1);
                while (itemCapacity / columns * (subRect.Height + spacing) > GameMain.GraphicsHeight * 0.5f)
                {
                    columns++;
                }

                int startX = slot.Rect.Center.X - (int)(subRect.Width * (columns / 2.0f) + spacing * ((columns - 1) / 2.0f));
                subRect.X = startX;
                int startY = dir < 0 ?
                    slot.EquipButtonRect.Y - subRect.Height - (int)(20 * UIScale) :
                    slot.EquipButtonRect.Bottom + (int)(10 * UIScale);
                subRect.Y = startY;

                float totalHeight = itemCapacity / columns * (subRect.Height + spacing);
                subInventory.openState = subInventory.HideTimer >= 0.5f ?
                    Math.Min(subInventory.openState + deltaTime * 5.0f, 1.0f) :
                    Math.Max(subInventory.openState - deltaTime * 3.0f, 0.0f);

                for (int i = 0; i < itemCapacity; i++)
                { 
                    subInventory.slots[i].Rect = subRect;
                    subInventory.slots[i].Rect.Location += new Point(0, (int)totalHeight * -dir);

                    subInventory.slots[i].DrawOffset = Vector2.SmoothStep( new Vector2(0, -50 * dir), new Vector2(0, totalHeight * dir), subInventory.openState);

                    subInventory.slots[i].InteractRect = subInventory.slots[i].Rect;
                    subInventory.slots[i].InteractRect.Inflate((int)(5 * UIScale), (int)(5 * UIScale));

                    if ((i + 1) % columns == 0)
                    {
                        subRect.X = startX;
                        subRect.Y += subRect.Height * dir;
                        subRect.Y += spacing * dir;
                    }
                    else
                    {
                        subRect.X = subInventory.slots[i].Rect.Right + spacing;
                    }
                }        
                slots[slotIndex].State = GUIComponent.ComponentState.Hover;
            }
            subInventory.isSubInventory = true;    
            subInventory.Update(deltaTime, cam, true);
        }


        public virtual void Draw(SpriteBatch spriteBatch, bool subInventory = false)
        {
            if (slots == null || isSubInventory != subInventory) return;

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) continue;

                //don't draw the item if it's being dragged out of the slot
                bool drawItem = draggingItem == null || draggingItem != Items[i] || slots[i].IsHighlighted;

                DrawSlot(spriteBatch, this, slots[i], Items[i], drawItem);
            }
        }

        protected static void DrawToolTip(SpriteBatch spriteBatch, string toolTip, Rectangle highlightedSlot)
        {
            int maxWidth = 300;

            toolTip = ToolBox.WrapText(toolTip, maxWidth, GUI.Font);

            Vector2 textSize = GUI.Font.MeasureString(toolTip);
            Vector2 rectSize = textSize * 1.2f;

            Vector2 pos = new Vector2(highlightedSlot.Right, highlightedSlot.Y);
            pos.X = (int)(pos.X + 3);
            pos.Y = (int)pos.Y - Math.Max((pos.Y + rectSize.Y) - GameMain.GraphicsHeight, 0);

            if (pos.X + rectSize.X > GameMain.GraphicsWidth) pos.X -= rectSize.X + highlightedSlot.Width;

            GUI.DrawRectangle(spriteBatch, pos, rectSize, Color.Black * 0.8f, true);
            GUI.Font.DrawString(spriteBatch, toolTip,
                new Vector2((int)(pos.X + rectSize.X * 0.5f), (int)(pos.Y + rectSize.Y * 0.5f)),
                Color.White, 0.0f,
                new Vector2((int)(textSize.X * 0.5f), (int)(textSize.Y * 0.5f)),
                1.0f, SpriteEffects.None, 0.0f);
        }


        public void DrawSubInventory(SpriteBatch spriteBatch, int slotIndex)
        {
            var item = Items[slotIndex];
            if (item == null) return;

            var container = item.GetComponent<ItemContainer>();
            if (container == null || !container.DrawInventory) return;

            if (container.Inventory.slots == null || !container.Inventory.isSubInventory) return;

            int itemCapacity = container.Capacity;

#if DEBUG
            System.Diagnostics.Debug.Assert(slotIndex >= 0 && slotIndex < Items.Length);
#else
            if (slotIndex < 0 || slotIndex >= Items.Length) return;
#endif

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            if (slots[slotIndex].SubInventoryDir > 0)
            {
                spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(
                    new Point(0, slots[slotIndex].Rect.Bottom),
                    new Point(GameMain.GraphicsWidth, (int)Math.Max(GameMain.GraphicsHeight - slots[slotIndex].Rect.Bottom, 0)));
            }
            else
            {
                spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(
                    new Point(0, 0),
                    new Point(GameMain.GraphicsWidth, slots[slotIndex].Rect.Y));
            }

            container.Inventory.Draw(spriteBatch, true);
            spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;

            container.InventoryBottomSprite?.Draw(spriteBatch,
                new Vector2(slots[slotIndex].Rect.Center.X, slots[slotIndex].Rect.Y) + slots[slotIndex].DrawOffset,
                0.0f, UIScale);

            container.InventoryTopSprite?.Draw(spriteBatch,
                new Vector2(
                    slots[slotIndex].Rect.Center.X, 
                    container.Inventory.slots[container.Inventory.slots.Length - 1].Rect.Y) + container.Inventory.slots[container.Inventory.slots.Length - 1].DrawOffset,
                0.0f, UIScale);

        }

        public static void UpdateDragging()
        {
            if (draggingItem != null && PlayerInput.LeftButtonReleased())
            {
                if (CharacterHealth.OpenHealthWindow != null && 
                    CharacterHealth.OpenHealthWindow.OnItemDropped(draggingItem, false))
                {
                    draggingItem = null;
                    return;
                }
                
                if (selectedSlot == null)
                {
                    draggingItem.ParentInventory?.CreateNetworkEvent();
                    draggingItem.Drop();
                    GUI.PlayUISound(GUISoundType.DropItem);
                }
                else if (selectedSlot.ParentInventory.Items[selectedSlot.SlotIndex] != draggingItem)
                {
                    Inventory selectedInventory = selectedSlot.ParentInventory;
                    int slotIndex = selectedSlot.SlotIndex;
                    if (selectedInventory.TryPutItem(draggingItem, slotIndex, true, true, Character.Controlled))
                    {
                        if (selectedInventory.slots != null) selectedInventory.slots[slotIndex].ShowBorderHighlight(Color.White, 0.1f, 0.4f);
                        GUI.PlayUISound(GUISoundType.PickItem);
                    }
                    else
                    {
                        if (selectedInventory.slots != null) selectedInventory.slots[slotIndex].ShowBorderHighlight(Color.Red, 0.1f, 0.9f);
                        GUI.PlayUISound(GUISoundType.PickItemFail);
                    }
                    selectedInventory.HideTimer = 1.0f;
                    if (selectedSlot.ParentInventory?.Owner is Item parentItem && parentItem.ParentInventory != null)
                    {
                        highlightedSubInventorySlots.Add(new SlotReference(
                            parentItem.ParentInventory, parentItem.ParentInventory.slots[Array.IndexOf(parentItem.ParentInventory.Items, parentItem)],
                            Array.IndexOf(parentItem.ParentInventory.Items, parentItem),
                            false, selectedSlot.ParentInventory));
                    }
                    draggingItem = null;
                    draggingSlot = null;
                }

                draggingItem = null;
            }
            
            if (selectedSlot != null && !selectedSlot.Slot.MouseOn())
            {
                selectedSlot = null;
            }
        }

        public static void DrawFront(SpriteBatch spriteBatch)
        {
            foreach (var slot in highlightedSubInventorySlots)
            {
                int slotIndex = Array.IndexOf(slot.ParentInventory.slots, slot.Slot);
                if (slotIndex > 0 && slotIndex < slot.ParentInventory.slots.Length)
                {
                    slot.ParentInventory.DrawSubInventory(spriteBatch, slotIndex);
                }
            }

            if (draggingItem != null)
            {
                if (draggingSlot == null || (!draggingSlot.MouseOn()))
                {
                    Rectangle dragRect = new Rectangle(
                        (int)(PlayerInput.MousePosition.X - 10 * UIScale),
                        (int)(PlayerInput.MousePosition.Y - 10 * UIScale),
                        (int)(80 * UIScale), (int)(80 * UIScale));

                    DrawSlot(spriteBatch, null, new InventorySlot(dragRect), draggingItem);
                }
            }

            if (selectedSlot != null)
            {
                Item item = selectedSlot.ParentInventory.Items[selectedSlot.SlotIndex];
                if (item != null)
                {
                    string toolTip = "";
                    if (GameMain.DebugDraw)
                    {
                        toolTip = item.ToString();
                    }
                    else
                    {
                        string description = item.Description;
                        if (item.Prefab.Identifier == "idcard")
                        {
                            string[] readTags = item.Tags.Split(',');
                            string idName = null;
                            string idJob = null;
                            foreach (string tag in readTags)
                            {
                                string[] s = tag.Split(':');
                                if (s[0] == "name")
                                    idName = s[1];
                                if (s[0] == "job")
                                    idJob = s[1];
                            }
                            if (idName != null)
                                description = "This belongs to " + idName + (idJob != null ? ", the " + idJob + "." : ".") + description;
                        }
                        toolTip = string.IsNullOrEmpty(description) ?
                            item.Name :
                            item.Name + '\n' + description;
                    }

                    Rectangle slotRect = selectedSlot.Slot.Rect;
                    slotRect.Location += selectedSlot.Slot.DrawOffset.ToPoint();
                    DrawToolTip(spriteBatch, toolTip, slotRect);
                }
            }

        }

        public static void DrawSlot(SpriteBatch spriteBatch, Inventory inventory, InventorySlot slot, Item item, bool drawItem = true)
        {
            Rectangle rect = slot.Rect;
            rect.Location += slot.DrawOffset.ToPoint();
            
            if (slot.BorderHighlightColor.A > 0)
            {
                rect.Inflate(rect.Width * (slot.BorderHighlightColor.A / 700.0f), rect.Height * (slot.BorderHighlightColor.A / 700.0f));
            }

            var itemContainer = item?.GetComponent<ItemContainer>();
            if (itemContainer != null && (itemContainer.InventoryTopSprite != null || itemContainer.InventoryBottomSprite != null))
            {
                if (!highlightedSubInventorySlots.Any(s => s.Slot == slot))
                {
                    itemContainer.InventoryBottomSprite?.Draw(spriteBatch, new Vector2(rect.Center.X, rect.Y), 0, UIScale);
                    itemContainer.InventoryTopSprite?.Draw(spriteBatch, new Vector2(rect.Center.X, rect.Y), 0, UIScale);
                }

                drawItem = false;
            }
            else
            {
                Sprite slotSprite = slot.SlotSprite ?? slotSpriteSmall;

                spriteBatch.Draw(slotSprite.Texture, rect, slotSprite.SourceRect, slot.IsHighlighted ? Color.White : Color.White * 0.8f);

                if (item != null && drawItem)
                {
                    if (item.Condition < item.Prefab.Health)
                    {
                        GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Bottom - 8, rect.Width, 8), Color.Black * 0.8f, true);
                        GUI.DrawRectangle(spriteBatch,
                            new Rectangle(rect.X, rect.Bottom - 8, (int)(rect.Width * item.Condition / item.Prefab.Health), 8),
                            Color.Lerp(Color.Red, Color.Green, item.Condition / 100.0f) * 0.8f, true);
                    }

                    if (itemContainer != null)
                    {
                        float containedState = itemContainer.Inventory.Capacity == 1 ?
                            (itemContainer.Inventory.Items[0] == null ? 0.0f : itemContainer.Inventory.Items[0].Condition / 100.0f) :
                            itemContainer.Inventory.Items.Count(i => i != null) / (float)itemContainer.Inventory.capacity;

                        int dir = slot.SubInventoryDir;
                        Rectangle containedIndicatorArea = new Rectangle(rect.X,
                            dir < 0 ? rect.Bottom + HUDLayoutSettings.Padding / 2 : rect.Y - HUDLayoutSettings.Padding / 2 - ContainedIndicatorHeight, rect.Width, ContainedIndicatorHeight);
                        containedIndicatorArea.Inflate(-4, 0);
                        
                        if (itemContainer.ContainedStateIndicator == null)
                        {
                            GUI.DrawRectangle(spriteBatch, containedIndicatorArea, Color.DarkGray * 0.8f, true);
                            GUI.DrawRectangle(spriteBatch,
                                new Rectangle(containedIndicatorArea.X, containedIndicatorArea.Y, (int)(containedIndicatorArea.Width * containedState), containedIndicatorArea.Height),
                                Color.Lerp(Color.Red, Color.Green, containedState) * 0.8f, true);
                        }
                        else
                        {
                            itemContainer.ContainedStateIndicator.Draw(spriteBatch, containedIndicatorArea.Location.ToVector2(),
                                Color.DarkGray * 0.8f, 
                                origin: Vector2.Zero,
                                rotate: 0.0f,
                                scale: new Vector2(containedIndicatorArea.Width / (float)itemContainer.ContainedStateIndicator.SourceRect.Width, containedIndicatorArea.Height / (float)itemContainer.ContainedStateIndicator.SourceRect.Height));
                     
                            spriteBatch.Draw(itemContainer.ContainedStateIndicator.Texture,
                                new Rectangle(containedIndicatorArea.Location, new Point((int)(containedIndicatorArea.Width * containedState), containedIndicatorArea.Height)),
                                new Rectangle(itemContainer.ContainedStateIndicator.SourceRect.Location, new Point((int)(itemContainer.ContainedStateIndicator.SourceRect.Width * containedState), itemContainer.ContainedStateIndicator.SourceRect.Height)),
                                Color.Lerp(Color.Red, Color.Green, containedState) * 0.8f);
                        }
                    }
                }
            }
            
            if (GameMain.DebugDraw) GUI.DrawRectangle(spriteBatch, rect, Color.White, false, 0, 1);

            if (slot.BorderHighlightColor != Color.Transparent)
            {
                Rectangle highlightRect = rect;
                highlightRect.Inflate(3, 3);

                GUI.DrawRectangle(spriteBatch, highlightRect, slot.BorderHighlightColor, false, 0, 5);
            }

            if (item != null && drawItem)
            {
                Sprite sprite = item.Prefab.InventoryIcon ?? item.Sprite;
                float scale = Math.Min(Math.Min((rect.Width - 10) / sprite.size.X, (rect.Height - 10) / sprite.size.Y), 3.0f);
                Vector2 itemPos = rect.Center.ToVector2();
                if (itemPos.Y > GameMain.GraphicsHeight)
                {
                    itemPos.Y -= Math.Min(
                        (itemPos.Y + sprite.size.Y / 2 * scale) - GameMain.GraphicsHeight,
                        (itemPos.Y - sprite.size.Y / 2 * scale) - rect.Y);
                }

                sprite.Draw(spriteBatch, itemPos, sprite == item.Sprite ? item.GetSpriteColor() : item.Prefab.InventoryIconColor, 0, scale);
                
                if (CharacterHealth.OpenHealthWindow != null)
                {
                    float treatmentSuitability = CharacterHealth.OpenHealthWindow.GetTreatmentSuitability(item);
                    float skill = Character.Controlled.GetSkillLevel("medical");
                    if (skill > 50.0f)
                    {
                        Rectangle highlightRect = rect;
                        highlightRect.Inflate(3, 3);

                        Color color = treatmentSuitability < 0.0f ?
                            Color.Lerp(Color.Transparent, Color.Red, -treatmentSuitability) :
                            Color.Lerp(Color.Transparent, Color.Green, treatmentSuitability);
                        GUI.DrawRectangle(spriteBatch, highlightRect, color * (((float)Math.Sin(Timing.TotalTime * 5.0f) + 1.0f) / 2.0f), false, 0, 5);
                    }
                }
            }

            if (inventory != null && Character.Controlled?.Inventory == inventory && slot.QuickUseKey != Keys.None)
            {
                GUI.DrawString(spriteBatch, rect.Location.ToVector2(), 
                    slot.QuickUseKey.ToString().Substring(1, 1), 
                    item == null ? Color.Gray : Color.White, 
                    Color.Black * 0.8f);
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            receivedItemIDs = new ushort[capacity];

            for (int i = 0; i < capacity; i++)
            {
                receivedItemIDs[i] = msg.ReadUInt16();
            }

            //delay applying the new state if less than 1 second has passed since this client last sent a state to the server
            //prevents the inventory from briefly reverting to an old state if items are moved around in quick succession

            //also delay if we're still midround syncing, some of the items in the inventory may not exist yet
            if (syncItemsDelay > 0.0f || GameMain.Client.MidRoundSyncing)
            {
                if (syncItemsCoroutine != null) CoroutineManager.StopCoroutines(syncItemsCoroutine);
                syncItemsCoroutine = CoroutineManager.StartCoroutine(SyncItemsAfterDelay());
            }
            else
            {
                if (syncItemsCoroutine != null)
                {
                    CoroutineManager.StopCoroutines(syncItemsCoroutine);
                    syncItemsCoroutine = null;
                }
                ApplyReceivedState();
            }
        }

        private IEnumerable<object> SyncItemsAfterDelay()
        {
            while (syncItemsDelay > 0.0f || (GameMain.Client != null && GameMain.Client.MidRoundSyncing))
            {
                syncItemsDelay -= CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }

            if (Owner.Removed || GameMain.Client == null)
            {
                yield return CoroutineStatus.Success;
            }

            ApplyReceivedState();

            yield return CoroutineStatus.Success;
        }

        private void ApplyReceivedState()
        {
            if (receivedItemIDs == null) return;

            for (int i = 0; i < capacity; i++)
            {
                if (receivedItemIDs[i] == 0 || (Entity.FindEntityByID(receivedItemIDs[i]) as Item != Items[i]))
                {
                    if (Items[i] != null) Items[i].Drop();
                    System.Diagnostics.Debug.Assert(Items[i] == null);
                }
            }

            for (int i = 0; i < capacity; i++)
            {
                if (receivedItemIDs[i] > 0)
                {
                    var item = Entity.FindEntityByID(receivedItemIDs[i]) as Item;
                    if (item == null) continue;

                    TryPutItem(item, i, true, true, null, false);
                }
            }

            receivedItemIDs = null;
        }
    }
}
