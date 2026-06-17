using DSAnimStudio.TaeEditor;
using DSAnimStudio.TaeEditor.TaeScripting;
using ImGuiNET;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;

namespace DSAnimStudio.ImguiOSD
{
    public abstract partial class Window
    {
        public class TaeScriptEditor : Window
        {
            public override SaveOpenStateTypes GetSaveOpenStateType() => SaveOpenStateTypes.SaveAlways;

            public override string NewImguiWindowTitle => "TAE Scripting Tool";

            private string scriptText = "# TAE Animation DSL Script\n# Example: Shift all events in anim a00_0000 by 10 frames\nMATCH anim \"a00_0000\" {\n    SHIFT ALL BY 10;\n}";
            private string consoleOutput = "Ready.";
            private ExecutionScope executionScope = ExecutionScope.CurrentAnimation;

            private readonly string[] examples = new string[]
            {
                "Shift All Events",
                "Add SpEffect Event at Start",
                "Remove SpEffect by Condition",
                "Modify SpEffect Parameter",
                "Copy Event Timing"
            };

            private readonly string[] exampleScripts = new string[]
            {
                "# Shift all events by 10 frames in all animations\nMATCH anim \"*\" {\n    SHIFT ALL BY 10;\n}",
                "# Add SpEffect (type 96) at frame range 0-30 in all matching animations\nMATCH anim \"a00_*\" {\n    ADD \"SpEffect\" AT 0-30;\n}",
                "# Remove all SpEffect (type 96) events where the first argument (ID) is 100\nMATCH anim \"*\" {\n    REMOVE \"SpEffect\" WHERE args[0] == 100;\n}",
                "# Modify SpEffect events where ID is 100 to set ID = 200 and timing to 10-40\nMATCH anim \"*\" {\n    MODIFY \"SpEffect\" WHERE args[0] == 100 SET args[0] = 200, start = 10, end = 40;\n}",
                "# Copy timing of SoundEffect (type 1) to VisualEffect (type 2) events\nMATCH anim \"*\" {\n    ADD \"VisualEffect\" AT COPY TIMING OF \"SoundEffect\";\n}"
            };

            protected override void Init()
            {
                IsOpen = false;
                UniqueImguiInstanceNoSavedSettings = false;
            }

            protected override void BuildContents(ref bool anyFieldFocused)
            {
                var proj = MainTaeScreen?.FileContainer?.Proj;
                if (proj == null)
                {
                    ImGui.Text("No active project loaded.");
                    return;
                }

                // Top Buttons: Load, Save, Examples
                if (ImGui.Button("Load Script..."))
                {
                    var dlg = new OpenFileDialog
                    {
                        Filter = "TAE Script files (*.txt;*.tae)|*.txt;*.tae|All files (*.*)|*.*",
                        Title = "Open TAE DSL Script"
                    };
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        scriptText = File.ReadAllText(dlg.FileName);
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Save Script As..."))
                {
                    var dlg = new SaveFileDialog
                    {
                        Filter = "TAE Script files (*.txt;*.tae)|*.txt;*.tae|All files (*.*)|*.*",
                        Title = "Save TAE DSL Script"
                    };
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        File.WriteAllText(dlg.FileName, scriptText);
                    }
                }

                ImGui.SameLine();
                ImGui.SetNextItemWidth(180);
                int selectedExample = -1;
                if (ImGui.BeginCombo("Examples", "Select Template..."))
                {
                    for (int i = 0; i < examples.Length; i++)
                    {
                        if (ImGui.Selectable(examples[i]))
                        {
                            selectedExample = i;
                        }
                    }
                    ImGui.EndCombo();
                }

                if (selectedExample >= 0)
                {
                    scriptText = exampleScripts[selectedExample];
                }

                ImGui.Separator();

                // Multiline Text input for script text
                ImGui.Text("Script Editor:");
                ImGui.InputTextMultiline("##TaeScriptContent", ref scriptText, 65536, new Vector2(-1, 200), ImGuiInputTextFlags.AllowTabInput);

                ImGui.Separator();

                // Execution options
                ImGui.Text("Execution Scope:");
                
                if (ImGui.RadioButton("Current Animation Only", executionScope == ExecutionScope.CurrentAnimation))
                {
                    executionScope = ExecutionScope.CurrentAnimation;
                }
                ImGui.SameLine();
                if (ImGui.RadioButton("Selected Category Only", executionScope == ExecutionScope.SelectedCategory))
                {
                    executionScope = ExecutionScope.SelectedCategory;
                }
                ImGui.SameLine();
                if (ImGui.RadioButton("All Loaded Animations", executionScope == ExecutionScope.AllAnimations))
                {
                    executionScope = ExecutionScope.AllAnimations;
                }

                ImGui.Spacing();

                // Execute Button
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.7f, 0.3f, 1.0f));
                if (ImGui.Button("Execute Script", new Vector2(-1, 30)))
                {
                    var currentAnim = MainTaeScreen?.Graph?.AnimRef;
                    var result = TaeScriptInterpreter.RunScript(scriptText, proj, executionScope, currentAnim);
                    consoleOutput = result.output;
                }
                ImGui.PopStyleColor(2);

                ImGui.Separator();

                // Console output
                ImGui.Text("Console Output:");
                ImGui.InputTextMultiline("##TaeScriptConsole", ref consoleOutput, 65536, new Vector2(-1, -1), ImGuiInputTextFlags.ReadOnly);
            }
        }
    }
}
