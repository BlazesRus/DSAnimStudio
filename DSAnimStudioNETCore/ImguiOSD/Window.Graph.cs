using DSAnimStudio.TaeEditor;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace DSAnimStudio.ImguiOSD
{
    public abstract partial class Window
    {
        public class Graph : Window
        {
            public override SaveOpenStateTypes GetSaveOpenStateType() => SaveOpenStateTypes.NoSave;

            public override string NewImguiWindowTitle => "Graph";

            protected override void Init()
            {
                
            }

            protected override void PreUpdate()
            {
                ClipDockTabArea = false;
                IsOpen = true;
                Flags = ImGuiWindowFlags.NoInputs;
                CustomBackgroundColor = System.Numerics.Vector4.Zero;
                CustomBorderColor_Focused = System.Numerics.Vector4.Zero;
                CustomBorderColor_Unfocused = System.Numerics.Vector4.Zero;
            }
            
            protected override void BuildContents(ref bool anyFieldFocused)
            {
                float cursorX = ImGui.GetCursorPosX() - 8;

                CustomBackgroundColor = System.Numerics.Vector4.Zero;
                
                if (MainTaeScreen.TaeAnimInfoIsClone)
                {
                    var thisWindowRect = GetRect(subtractTitleBar: false);
                    if (ImGuiDebugDrawer.FakeButton(new Vector2(thisWindowRect.X + 10, thisWindowRect.Y + (ImGui.GetCursorPosY() / Main.DPI) + 1), 
                            new Vector2((int)(170), 20) - new Vector2(2,2), "Go To Original (F4 Key)", 0, out bool isHovering))
                    {
                        MainTaeScreen.RequestGoToEventSource = true;
                        
                    }
                    cursorX += (170 + 8) * Main.DPI;
                }
                
                ImGui.SetCursorPosX(cursorX);
                
                ImGui.Text("  " + (MainTaeScreen?.SelectedTaeAnimInfoText ?? ""));
                //ImGui.Text(OSD.FocusedWindow?.NewImguiWindowTitle ?? "");
                
                var dsid = ImGui.GetID("DockSpace_NewActionGraph");
                // ImGuiDockNodeFlags (1 << 12) is ImGuiDockNodeFlags_NoTabBar
                ImGui.DockSpace(dsid, new Vector2(0, 0), ImGuiDockNodeFlags.PassthruCentralNode | (ImGuiDockNodeFlags)(1 << 12));

                
                
                ImGui.SetWindowSize(new Vector2(300, 300), ImGuiCond.FirstUseEver);
                //ImGui.Text(" ");
                
                BuildGraphContextMenu(MainTaeScreen?.Graph);
            }

            private void BuildGraphContextMenu(NewGraph graph)
            {
                if (graph == null) return;

                if (graph.RequestContextMenu)
                {
                    ImGui.OpenPopup("GraphContextMenu");
                    graph.RequestContextMenu = false;
                }

                if (ImGui.BeginPopup("GraphContextMenu"))
                {
                    graph.ContextMenuOpen = true;
                    int trackIndex = graph.ContextMenuTrackIndex;
                    var clickedAction = graph.ContextMenuAction;
                    var anim = graph.AnimRef;
                    var undoMan = OSD.Tae.GetUndoManForSpecificAnim(anim);
                    bool isGhost = graph.IsGhostGraph;

                    if (clickedAction != null)
                    {
                        ImGui.TextDisabled($"Action: Type {clickedAction.Type}");

                        if (ImGui.MenuItem("Change Type (F1)", !isGhost))
                        {
                            OSD.Tae.ChangeTypeOfEvent(clickedAction);
                        }

                        if (ImGui.MenuItem("Copy Action", true))
                        {
                            if (!OSD.Tae.NewSelectedActions.Contains(clickedAction))
                            {
                                OSD.Tae.NewSelectedActions.Clear();
                                OSD.Tae.NewSelectedActions.Add(clickedAction);
                            }
                            graph.InputMan.DoCopy();
                        }

                        if (ImGui.MenuItem("Delete Action (Delete)", !isGhost))
                        {
                            if (!OSD.Tae.NewSelectedActions.Contains(clickedAction))
                            {
                                OSD.Tae.NewSelectedActions.Clear();
                                OSD.Tae.NewSelectedActions.Add(clickedAction);
                            }
                            graph.InputMan.DeleteSelectedActions();
                        }

                        ImGui.Separator();
                    }

                    if (trackIndex >= 0)
                    {
                        var track = anim.SAFE_GetActionTrackByIndex(trackIndex);
                        string trackName = track?.Info?.DisplayName ?? $"Row {trackIndex + 1}";
                        ImGui.TextDisabled($"Track: {trackName}");

                        if (ImGui.MenuItem("Edit Track Properties...", true))
                        {
                            DialogManager.ShowTaeActionTrackPropertiesEditor(anim, trackIndex, track);
                        }

                        if (ImGui.MenuItem("Insert Track Above", !isGhost))
                        {
                            undoMan.NewAction(() =>
                            {
                                var newTrack = new DSAProj.ActionTrack()
                                {
                                    Info = new DSAProj.EditorInfo("New Track"),
                                    TrackData = new SoulsAssetPipeline.Animation.TAE.ActionTrack.ActionTrackDataStruct()
                                    { DataType = SoulsAssetPipeline.Animation.TAE.ActionTrack.ActionTrackDataType.TrackData0 },
                                    TrackType = 0,
                                };
                                anim.SafeAccessActionTracks(tracks =>
                                {
                                    tracks.Insert(trackIndex, newTrack);
                                });
                                anim.SafeAccessActions(actions =>
                                {
                                    foreach (var act in actions)
                                    {
                                        if (act.TrackIndex >= trackIndex)
                                            act.TrackIndex++;
                                    }
                                });
                            }, actionDescription: $"Insert track above row {trackIndex + 1}");
                        }

                        if (ImGui.MenuItem("Insert Track Below", !isGhost))
                        {
                            undoMan.NewAction(() =>
                            {
                                var newTrack = new DSAProj.ActionTrack()
                                {
                                    Info = new DSAProj.EditorInfo("New Track"),
                                    TrackData = new SoulsAssetPipeline.Animation.TAE.ActionTrack.ActionTrackDataStruct()
                                    { DataType = SoulsAssetPipeline.Animation.TAE.ActionTrack.ActionTrackDataType.TrackData0 },
                                    TrackType = 0,
                                };
                                anim.SafeAccessActionTracks(tracks =>
                                {
                                    tracks.Insert(trackIndex + 1, newTrack);
                                });
                                anim.SafeAccessActions(actions =>
                                {
                                    foreach (var act in actions)
                                    {
                                        if (act.TrackIndex >= trackIndex + 1)
                                            act.TrackIndex++;
                                    }
                                });
                            }, actionDescription: $"Insert track below row {trackIndex + 1}");
                        }

                        if (ImGui.MenuItem("Delete Track", !isGhost))
                        {
                            DialogManager.AskYesNo("Delete Track",
                                $"Are you sure you want to delete track '{trackName}'?\nAll actions on this track will be deleted.",
                                choice =>
                                {
                                    if (choice)
                                    {
                                        undoMan.NewAction(() =>
                                        {
                                            anim.SafeAccessActionTracks(tracks =>
                                            {
                                                if (trackIndex >= 0 && trackIndex < tracks.Count)
                                                    tracks.RemoveAt(trackIndex);
                                            });
                                            anim.SafeAccessActions(actions =>
                                            {
                                                var toRemove = actions.Where(a => a.TrackIndex == trackIndex).ToList();
                                                foreach (var act in toRemove)
                                                {
                                                    actions.Remove(act);
                                                    OSD.Tae.NewSelectedActions.Remove(act);
                                                }
                                                foreach (var act in actions)
                                                {
                                                    if (act.TrackIndex > trackIndex)
                                                        act.TrackIndex--;
                                                }
                                            });
                                        }, actionDescription: $"Delete track '{trackName}'");
                                    }
                                });
                        }

                        if (ImGui.MenuItem("Duplicate Track", !isGhost))
                        {
                            undoMan.NewAction(() =>
                            {
                                var originalTrack = anim.SAFE_GetActionTrackByIndex(trackIndex);
                                if (originalTrack != null)
                                {
                                    var clonedTrack = originalTrack.GetClone();
                                    anim.SafeAccessActionTracks(tracks =>
                                    {
                                        tracks.Insert(trackIndex + 1, clonedTrack);
                                    });
                                    anim.SafeAccessActions(actions =>
                                    {
                                        foreach (var act in actions)
                                        {
                                            if (act.TrackIndex >= trackIndex + 1)
                                                act.TrackIndex++;
                                        }
                                        var toDuplicate = actions.Where(a => a.TrackIndex == trackIndex).ToList();
                                        foreach (var act in toDuplicate)
                                        {
                                            var clonedAction = act.GetClone();
                                            clonedAction.TrackIndex = trackIndex + 1;
                                            anim.INNER_AddAction(clonedAction);
                                        }
                                    });
                                }
                            }, actionDescription: $"Duplicate track '{trackName}'");
                        }

                        ImGui.Separator();
                    }

                    if (ImGui.MenuItem("Add Track at End", !isGhost))
                    {
                        undoMan.NewAction(() =>
                        {
                            var newTrack = new DSAProj.ActionTrack()
                            {
                                Info = new DSAProj.EditorInfo("New Track"),
                                TrackData = new SoulsAssetPipeline.Animation.TAE.ActionTrack.ActionTrackDataStruct()
                                { DataType = SoulsAssetPipeline.Animation.TAE.ActionTrack.ActionTrackDataType.TrackData0 },
                                TrackType = 0,
                            };
                            anim.SAFE_AddActionTrack(newTrack);
                        }, actionDescription: "Add track at end");
                    }

                    if (ImGui.MenuItem("Paste Action(s)", !isGhost))
                    {
                        graph.InputMan.DoPaste(false);
                    }

                    if (ImGui.MenuItem("Sort Tracks", true))
                    {
                        OSD.Tae.ResortTracks_Anim();
                    }

                    if (ImGui.MenuItem("Regen Track Names", true))
                    {
                        OSD.Tae.RegenTrackNames_Anim();
                    }

                    ImGui.EndPopup();
                }
                else
                {
                    graph.ContextMenuOpen = false;
                }
            }
        }
    }
}
