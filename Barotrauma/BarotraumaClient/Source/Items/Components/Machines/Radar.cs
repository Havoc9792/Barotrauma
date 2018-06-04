﻿using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Radar : Powered, IServerSerializable, IClientSerializable
    {
        private GUITickBox isActiveTickBox;

        private float displayBorderSize;

        private List<RadarBlip> radarBlips;

        private float prevPingRadius;

        private float prevPassivePingRadius;

        private Vector2 center;
        private float displayRadius;
        private float displayScale;

        private static Color[] blipColorGradient =
        {
            Color.TransparentBlack,
            new Color(0, 50, 160),
            new Color(0, 133, 166),
            new Color(2, 159, 30),
            new Color(255, 255, 255)
        };

        partial void InitProjSpecific(XElement element)
        {
            displayBorderSize = element.GetAttributeFloat("displaybordersize", 0.0f);

            radarBlips = new List<RadarBlip>();
            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), GuiFrame.RectTransform, Anchor.Center), style: null)
            {
                CanBeFocused = false
            };

            isActiveTickBox = new GUITickBox(new RectTransform(new Point(20, 20), paddedFrame.RectTransform), "Active Sonar")
            {
                OnSelected = (GUITickBox box) =>
                {
                    if (GameMain.Server != null)
                    {
                        item.CreateServerEvent(this);
                    }
                    else if (GameMain.Client != null)
                    {
                        item.CreateClientEvent(this);
                        correctionTimer = CorrectionDelay;
                    }
                    IsActive = box.Selected;

                    return true;
                }
            };

            new GUICustomComponent(new RectTransform(new Point(GuiFrame.Rect.Height, GuiFrame.Rect.Width), GuiFrame.RectTransform, Anchor.CenterRight) { AbsoluteOffset = new Point(10, 0) },
                (spriteBatch, guiCustomComponent) => { DrawRadar(spriteBatch, guiCustomComponent.Rect); }, null);

            GuiFrame.CanBeFocused = false;
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character, float deltaTime)
        {
            for (int i = radarBlips.Count - 1; i >= 0; i--)
            {
                radarBlips[i].FadeTimer -= deltaTime * 0.5f;
                if (radarBlips[i].FadeTimer <= 0.0f) radarBlips.RemoveAt(i);
            }

            if (IsActive)
            {
                float pingRadius = displayRadius * pingState;
                Ping(item.WorldPosition, pingRadius, prevPingRadius, displayScale, range, 2.0f);
                prevPingRadius = pingRadius;
                return;
            }

            float passivePingRadius = (float)Math.Sin(Timing.TotalTime * 10);
            if (passivePingRadius > 0.0f)
            {
                foreach (AITarget t in AITarget.List)
                {
                    if (t.SoundRange <= 0.0f) continue;

                    if (Vector2.Distance(t.WorldPosition, item.WorldPosition) < t.SoundRange)
                    {
                        Ping(t.WorldPosition, t.SoundRange * passivePingRadius * 0.2f, t.SoundRange * prevPassivePingRadius * 0.2f, displayScale, t.SoundRange, 0.5f);
                        radarBlips.Add(new RadarBlip(t.WorldPosition, 1.0f, 1.0f));
                    }
                }
            }
            prevPassivePingRadius = passivePingRadius;
        }
        
        private void DrawRadar(SpriteBatch spriteBatch, Rectangle rect)
        {
            displayBorderSize = 0.2f;
               center = new Vector2(rect.X + rect.Width * 0.5f, rect.Center.Y);
            displayRadius = (rect.Width / 2.0f) * (1.0f - displayBorderSize);
            displayScale = displayRadius / range;

            if (IsActive)
            {
                pingCircle.Draw(spriteBatch, center, Color.White * (1.0f - pingState), 0.0f, (displayRadius * 2 / pingCircle.size.X) * pingState);
            }

            if (item.Submarine != null && !DetectSubmarineWalls)
            {
                float simScale = displayScale * Physics.DisplayToSimRation;

                foreach (Submarine submarine in Submarine.Loaded)
                {
                    if (submarine != item.Submarine && !submarine.DockedTo.Contains(item.Submarine)) continue;
                    if (submarine.HullVertices == null) continue;

                    Vector2 offset = ConvertUnits.ToSimUnits(submarine.WorldPosition - item.WorldPosition);

                    for (int i = 0; i < submarine.HullVertices.Count; i++)
                    {
                        Vector2 start = (submarine.HullVertices[i] + offset) * simScale;
                        start.Y = -start.Y;
                        Vector2 end = (submarine.HullVertices[(i + 1) % submarine.HullVertices.Count] + offset) * simScale;
                        end.Y = -end.Y;

                        GUI.DrawLine(spriteBatch, center + start, center + end, Color.LightBlue);
                    }
                }
            }

            if (radarBlips.Count > 0)
            {
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);

                foreach (RadarBlip radarBlip in radarBlips)
                {
                    DrawBlip(spriteBatch, radarBlip, center, radarBlip.FadeTimer / 2.0f);
                }

                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, GameMain.ScissorTestEnable);
            }

            if (GameMain.DebugDraw)
            {
                GUI.DrawString(spriteBatch, rect.Location.ToVector2(), radarBlips.Count.ToString(), Color.White);
            }

            if (screenOverlay != null)
            {
                screenOverlay.Draw(spriteBatch, center, 0.0f, rect.Width / screenOverlay.size.X);
            }

            if (GameMain.GameSession == null) return;

            DrawMarker(spriteBatch,
                GameMain.GameSession.StartLocation.Name,
                (Level.Loaded.StartPosition - item.WorldPosition), displayScale, center, (rect.Width * 0.5f));

            DrawMarker(spriteBatch,
                GameMain.GameSession.EndLocation.Name,
                (Level.Loaded.EndPosition - item.WorldPosition), displayScale, center, (rect.Width * 0.5f));

            if (GameMain.GameSession.Mission != null)
            {
                var mission = GameMain.GameSession.Mission;

                if (!string.IsNullOrWhiteSpace(mission.RadarLabel) && mission.RadarPosition != Vector2.Zero)
                {
                    DrawMarker(spriteBatch,
                        mission.RadarLabel,
                        mission.RadarPosition - item.WorldPosition, displayScale, center, (rect.Width * 0.55f));
                }
            }

            foreach (Submarine sub in Submarine.Loaded)
            {
                if (!sub.OnRadar) continue;
                if (item.Submarine == sub || sub.DockedTo.Contains(item.Submarine)) continue;
                if (sub.WorldPosition.Y > Level.Loaded.Size.Y) continue;

                DrawMarker(spriteBatch, sub.Name, sub.WorldPosition - item.WorldPosition, displayScale, center, (rect.Width * 0.45f));
            }

            if (!GameMain.DebugDraw) return;

            var steering = item.GetComponent<Steering>();
            if (steering == null || steering.SteeringPath == null) return;

            Vector2 prevPos = Vector2.Zero;

            foreach (WayPoint wp in steering.SteeringPath.Nodes)
            {
                Vector2 pos = (wp.Position - item.WorldPosition) * displayScale;
                if (pos.Length() > displayRadius) continue;

                pos.Y = -pos.Y;
                pos += center;

                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X - 3 / 2, (int)pos.Y - 3, 6, 6), (steering.SteeringPath.CurrentNode == wp) ? Color.LightGreen : Color.Green, false);

                if (prevPos != Vector2.Zero)
                {
                    GUI.DrawLine(spriteBatch, pos, prevPos, Color.Green);
                }

                prevPos = pos;
            }
        }


        private void Ping(Vector2 pingSource, float pingRadius, float prevPingRadius, float displayScale, float range, float pingStrength = 1.0f)
        {
            float prevPingRadiusSqr = prevPingRadius * prevPingRadius;
            float pingRadiusSqr = pingRadius * pingRadius;

            //inside a hull -> only show the edges of the hull
            if (item.CurrentHull != null && DetectSubmarineWalls)
            {
                CreateBlipsForLine(
                    new Vector2(item.CurrentHull.WorldRect.X, item.CurrentHull.WorldRect.Y), 
                    new Vector2(item.CurrentHull.WorldRect.Right, item.CurrentHull.WorldRect.Y), 
                    pingRadius, prevPingRadius, 50.0f, 5.0f, range, 2.0f);

                CreateBlipsForLine(
                    new Vector2(item.CurrentHull.WorldRect.X, item.CurrentHull.WorldRect.Y - item.CurrentHull.Rect.Height),
                    new Vector2(item.CurrentHull.WorldRect.Right, item.CurrentHull.WorldRect.Y - item.CurrentHull.Rect.Height),
                    pingRadius, prevPingRadius, 50.0f, 5.0f, range, 2.0f);

                CreateBlipsForLine(
                    new Vector2(item.CurrentHull.WorldRect.X, item.CurrentHull.WorldRect.Y),
                    new Vector2(item.CurrentHull.WorldRect.X, item.CurrentHull.WorldRect.Y - item.CurrentHull.Rect.Height),
                    pingRadius, prevPingRadius, 50.0f, 5.0f, range, 2.0f);

                CreateBlipsForLine(
                    new Vector2(item.CurrentHull.WorldRect.Right, item.CurrentHull.WorldRect.Y),
                    new Vector2(item.CurrentHull.WorldRect.Right, item.CurrentHull.WorldRect.Y - item.CurrentHull.Rect.Height),
                    pingRadius, prevPingRadius, 50.0f, 5.0f, range, 2.0f);

                return;
            }

            foreach (Submarine submarine in Submarine.Loaded)
            {
                if (item.Submarine == submarine && !DetectSubmarineWalls) continue;
                if (item.Submarine != null && item.Submarine.DockedTo.Contains(submarine)) continue;
                if (submarine.HullVertices == null) continue;

                for (int i = 0; i < submarine.HullVertices.Count; i++)
                {
                    Vector2 start = ConvertUnits.ToDisplayUnits(submarine.HullVertices[i]);
                    Vector2 end = ConvertUnits.ToDisplayUnits(submarine.HullVertices[(i + 1) % submarine.HullVertices.Count]);

                    if (item.Submarine == submarine)
                    {
                        start += Rand.Vector(500.0f);
                        end += Rand.Vector(500.0f);
                    }

                    CreateBlipsForLine(
                        start + submarine.WorldPosition,
                        end + submarine.WorldPosition,
                        pingRadius, prevPingRadius,
                        200.0f, 2.0f, range, 1.0f);
                }
            }

            if (Level.Loaded != null && (item.CurrentHull == null || !DetectSubmarineWalls))
            {
                if (Level.Loaded.Size.Y - pingSource.Y < range)
                {
                    CreateBlipsForLine(
                        new Vector2(pingSource.X - range, Level.Loaded.Size.Y),
                        new Vector2(pingSource.X + range, Level.Loaded.Size.Y),
                        pingRadius, prevPingRadius,
                        250.0f, 150.0f, range, pingStrength);
                }

                List<Voronoi2.VoronoiCell> cells = Level.Loaded.GetCells(pingSource, 7);
                foreach (Voronoi2.VoronoiCell cell in cells)
                {
                    foreach (Voronoi2.GraphEdge edge in cell.edges)
                    {
                        if (!edge.isSolid) continue;
                        float cellDot = Vector2.Dot(cell.Center - pingSource, (edge.Center + cell.Translation) - cell.Center);
                        if (cellDot > 0) continue;

                        float facingDot = Vector2.Dot(
                            Vector2.Normalize(edge.point1 - edge.point2),
                            Vector2.Normalize(cell.Center - pingSource));

                        CreateBlipsForLine(
                            edge.point1 + cell.Translation,
                            edge.point2 + cell.Translation,
                            pingRadius, prevPingRadius,
                            350.0f, 3.0f * (Math.Abs(facingDot) + 1.0f), range, pingStrength);
                    }
                }

                foreach (RuinGeneration.Ruin ruin in Level.Loaded.Ruins)
                {
                    if (!MathUtils.CircleIntersectsRectangle(pingSource, range, ruin.Area)) continue;

                    foreach (var ruinShape in ruin.RuinShapes)
                    {
                        foreach (RuinGeneration.Line wall in ruinShape.Walls)
                        {
                            float cellDot = Vector2.Dot(
                                Vector2.Normalize(ruinShape.Center - pingSource),
                                Vector2.Normalize((wall.A + wall.B) / 2.0f - ruinShape.Center));
                            if (cellDot > 0) continue;

                            CreateBlipsForLine(
                                wall.A, wall.B,
                                pingRadius, prevPingRadius,
                                100.0f, 1000.0f, range, pingStrength);
                        }
                    }
                }
            }

            foreach (Item item in Item.ItemList)
            {
                if (item.CurrentHull == null && item.Prefab.RadarSize > 0.0f)
                {
                    float pointDist = ((item.WorldPosition - pingSource) * displayScale).LengthSquared();

                    if (pointDist > prevPingRadiusSqr && pointDist < pingRadiusSqr)
                    {
                        var blip = new RadarBlip(
                            item.WorldPosition + Rand.Vector(item.Prefab.RadarSize),
                            MathHelper.Clamp(item.Prefab.RadarSize, 0.1f, pingStrength),
                            MathHelper.Clamp(item.Prefab.RadarSize * 0.1f, 0.1f, 10.0f));
                        radarBlips.Add(blip);
                    }
                }
            }

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull != null || !c.Enabled) continue;
                if (DetectSubmarineWalls && c.AnimController.CurrentHull == null && item.CurrentHull != null) continue;

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    float pointDist = ((limb.WorldPosition - pingSource) * displayScale).LengthSquared();

                    if (limb.SimPosition == Vector2.Zero || pointDist > displayRadius * displayRadius) continue;

                    if (pointDist > prevPingRadiusSqr && pointDist < pingRadiusSqr)
                    {
                        var blip = new RadarBlip(
                            limb.WorldPosition + Rand.Vector(limb.Mass / 10.0f), 
                            MathHelper.Clamp(limb.Mass, 0.1f, pingStrength), 
                            MathHelper.Clamp(limb.Mass * 0.1f, 0.1f, 2.0f));
                        radarBlips.Add(blip);
                    }
                }
            }
        }

        private void CreateBlipsForLine(Vector2 point1, Vector2 point2, float pingRadius, float prevPingRadius,
            float lineStep, float zStep, float range, float pingStrength)
        {
            float length = (point1 - point2).Length();

            Vector2 lineDir = (point2 - point1) / length;

            range *= displayScale;

            for (float x = 0; x < length; x += lineStep * Rand.Range(0.8f, 1.2f))
            {
                Vector2 point = point1 + lineDir * x;
                //point += cell.Translation;

                float pointDist = Vector2.Distance(item.WorldPosition, point) * displayScale;

                if (pointDist > displayRadius) continue;
                if (pointDist < prevPingRadius || pointDist > pingRadius) continue;

                float alpha = pingStrength * Rand.Range(1.5f, 2.0f);
                for (float z = 0; z < displayRadius - pointDist * displayScale; z += zStep)
                {
                    Vector2 pos = point + Rand.Vector(150.0f) + Vector2.Normalize(point - item.WorldPosition) * z / displayScale;
                    float fadeTimer = alpha * (1.0f - pointDist / range);

                    int minDist = 200;
                    radarBlips.RemoveAll(b => b.FadeTimer < fadeTimer && Math.Abs(pos.X - b.Position.X) < minDist && Math.Abs(pos.Y - b.Position.Y) < minDist);

                    var blip = new RadarBlip(pos, fadeTimer, 1.0f + ((pointDist+z) / displayRadius));

                    radarBlips.Add(blip);
                    zStep += 0.5f;

                    if (z == 0)
                    {
                        alpha = Math.Min(alpha - 0.5f, 1.5f);
                    }
                    else
                    {
                        alpha -= 0.1f;
                    }

                    if (alpha < 0) break;
                }

            }
        }

        private void DrawBlip(SpriteBatch spriteBatch, RadarBlip blip, Vector2 center, float strength)
        {
            strength = MathHelper.Clamp(strength, 0.0f, 1.0f);
            
            float scaledT = strength * (blipColorGradient.Length - 1);
            Color color = Color.Lerp(blipColorGradient[(int)scaledT], blipColorGradient[(int)Math.Min(scaledT + 1, blipColorGradient.Length - 1)], (scaledT - (int)scaledT));

            Vector2 pos = (blip.Position - item.WorldPosition) * displayScale;
            pos.Y = -pos.Y;

            float posDist = pos.Length();
            if (posDist > displayRadius)
            {
                blip.FadeTimer = 0.0f;
                return;
            }

            if (radarBlip == null)
            {
                GUI.DrawRectangle(spriteBatch, center + pos, Vector2.One * 4, Color.Magenta, true);
                return;
            }

            Vector2 dir = pos / posDist;

            Vector2 normal = new Vector2(dir.Y, -dir.X);
            float scale = (strength + 3.0f) * blip.Scale;

            radarBlip.Draw(spriteBatch, center + pos, color, radarBlip.Origin, MathUtils.VectorToAngle(pos),
                new Vector2(scale * 0.5f, scale) * 0.04f, SpriteEffects.None, 0);

            pos += Rand.Range(0.0f, 1.0f) * dir + Rand.Range(-scale, scale) * normal;

            radarBlip.Draw(spriteBatch, center + pos, color * 0.5f, radarBlip.Origin, 0, scale * 0.08f, SpriteEffects.None, 0);
        }

        private void DrawMarker(SpriteBatch spriteBatch, string label, Vector2 position, float scale, Vector2 center, float radius)
        {
            float dist = position.Length();

            position *= scale;
            position.Y = -position.Y;

            float textAlpha = MathHelper.Clamp(1.5f - dist / 50000.0f, 0.5f, 1.0f);

            Vector2 dir = Vector2.Normalize(position);
            Vector2 markerPos = (dist * scale > radius) ? dir * radius : position;
            markerPos += center;

            markerPos.X = (int)markerPos.X;
            markerPos.Y = (int)markerPos.Y;

            if (!GuiFrame.Children.First().Rect.Contains(markerPos))
            {
                Vector2? intersection = MathUtils.GetLineRectangleIntersection(center, markerPos, GuiFrame.Children.First().Rect);
                if (intersection.HasValue) markerPos = intersection.Value;                
            }

            GUI.DrawRectangle(spriteBatch, new Rectangle((int)markerPos.X, (int)markerPos.Y, 5, 5), Color.LightBlue);

            string wrappedLabel = ToolBox.WrapText(label, 150, GUI.SmallFont);
            wrappedLabel += "\n" + ((int)(dist * Physics.DisplayToRealWorldRatio) + " m");

            Vector2 labelPos = markerPos;
            Vector2 textSize = GUI.SmallFont.MeasureString(wrappedLabel);

            //flip the text to left side when the marker is on the left side or goes outside the right edge of the interface
            if (dir.X < 0.0f || labelPos.X + textSize.X + 10 > GuiFrame.Rect.X) labelPos.X -= textSize.X + 10;

            GUI.DrawString(spriteBatch,
                new Vector2(labelPos.X + 10, labelPos.Y),
                wrappedLabel,
                Color.LightBlue * textAlpha, Color.Black * textAlpha * 0.8f,
                2, GUI.SmallFont);
        }
        
        public void ClientWrite(Lidgren.Network.NetBuffer msg, object[] extraData = null)
        {
            msg.Write(IsActive);
        }
        
        public void ClientRead(ServerNetObject type, Lidgren.Network.NetBuffer msg, float sendingTime)
        {
            if (correctionTimer > 0.0f)
            {
                StartDelayedCorrection(type, msg.ExtractBits(1), sendingTime);
                return;
            }

            IsActive = msg.ReadBoolean();
            isActiveTickBox.Selected = IsActive;
        }
    }

    class RadarBlip
    {
        public float FadeTimer;
        public Vector2 Position;
        public float Scale;

        public RadarBlip(Vector2 pos, float fadeTimer, float scale)
        {
            Position = pos;
            FadeTimer = Math.Max(fadeTimer, 0.0f);
            Scale = scale;
        }
    }
}
