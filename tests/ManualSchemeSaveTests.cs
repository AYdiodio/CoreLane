using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void ManualSchemeSaveTests(string working)
        {
            Topology topology = Topology.Parse(Fixture(false));
            Func<string, ulong, SchemeRule> rule = (path, mask) => new SchemeRule { Application = new SavedApplication { Name = Path.GetFileName(path), Paths = new List<string> { path } }, MaskHex = mask.ToString("X16") };
            Case("save scheme UI: rule card hugs short content and uses the shared scroll rail for long lists", delegate
            {
                var store = new SchemeStore(Path.Combine(working, "save-layout", "schemes.json"));
                SchemeRule[] many = Enumerable.Range(1, 12).Select(i => rule(@"C:\ManualFixture\app" + i + ".exe", 0xffff)).ToArray();
                using (var single = new ManualSchemeSaveDialog(store, topology, many.Take(1).ToArray(), saveSession: true))
                using (var multiple = new ManualSchemeSaveDialog(store, topology, many, saveSession: true))
                {
                    single.Show(); Application.DoEvents();
                    var list = ChildControls(single).OfType<ManualRuleList>().Single();
                    var target = ChildControls(single).OfType<CardPanel>().Single(card => card.AccessibleName == "方案目标");
                    var picker = ChildControls(single).OfType<CapsuleSchemePicker>().Single();
                    var name = ChildControls(single).OfType<TextBox>().Single();
                    Check(picker.Left == name.Parent.Left && picker.Width == name.Parent.Width, "destination picker and new name have the same edges");
                    var nameLabel = ChildControls(single).OfType<Label>().Single(item => item.Text == "方案名称");
                    Check(picker.Height == name.Parent.Height && nameLabel.Top == name.Parent.Top && nameLabel.Height == name.Parent.Height &&
                        Math.Abs(name.Top * 2 + name.Height - picker.Height) <= 1 && nameLabel.Width >= TextRenderer.MeasureText(nameLabel.Text, nameLabel.Font).Width,
                        "both capsules share a height and the name and label are vertically centered without wrapping");
                    Check(target.Left == list.Parent.Parent.Left && target.Width == list.Parent.Parent.Width, "the destination and preview share the same card edges");
                    Check(list.Height == list.ContentHeight && list.MaximumScroll == 0, "one rule leaves no blank card area");
                    int expanded = target.Height;
                    name.Text = "游戏";
                    Check(!((Control)single.AcceptButton).Enabled, "the reserved name cannot be saved as a new scheme");
                    picker.SelectedIndex = picker.Items.FindIndex(item => item.ToString() == "游戏");
                    Check(!name.Visible && target.Height < expanded && ((Control)single.AcceptButton).Enabled, "switching to an existing target collapses the name and permits saving");
                    picker.SelectedIndex = 0;
                    Check(name.Visible && target.Height == expanded, "the name reappears when saving a new scheme: " + name.Visible + ", " + target.Height + " vs " + expanded);
                    single.Close();

                    multiple.Show(); Application.DoEvents();
                    list = ChildControls(multiple).OfType<ManualRuleList>().Single();
                    Check(list.Items.Count == many.Length && list.MaximumScroll > 0, "long list is scrollable without changing its rule count");
                    foreach (int width in new[] { multiple.MinimumSize.Width, multiple.Width + 65 })
                    {
                        multiple.Size = new Size(width, multiple.Height);
                        Application.DoEvents();
                        list.ScrollTo(list.MaximumScroll);
                        Control last = list.Items[list.Items.Count - 1];
                        Check(last.Bottom - list.ScrollOffset <= list.ClientSize.Height && last.Width == list.ContentWidth,
                            "last rule fits within the preview when scrolled to the end at either width");
                        Check(last.Controls.Cast<Control>().All(control => last.ClientRectangle.Contains(control.Bounds) && control.Height > 0) &&
                            ChildControls(last).OfType<Label>().All(label => label.Height >= label.Font.Height),
                            "the icon, CPU range and action fit within each rule row at either width");
                    }
                    multiple.Close();
                    Check(!File.Exists(Path.Combine(working, "save-layout", "schemes.json")), "layout inspection does not save data");
                }
            });
            Case("manual save: immutable success snapshot, new and merged schemes preserve unrelated rules and startup choice", delegate
            {
                SchemeStore store = new SchemeStore(Path.Combine(working,"manual-save","schemes.json"));
                ProcessRow row = new ProcessRow { Pid=999990, Created=1, Name="A.exe", ExecutablePath=@"C:\ManualFixture\A.exe" };
                List<SchemeRule> captured = ManualSchemeRules.Capture(new[] { row, new ProcessRow { Pid=999991,Created=1,Name="unknown.exe" } },8, delegate { return null; });
                row.ExecutablePath=@"C:\Unrelated\new.exe"; row.Mask=16;
                Check(captured.Count==1 && captured[0].Mask==8 && captured[0].Application.Paths.Single()==@"C:\ManualFixture\A.exe", "success snapshot owns paths and confirmed target; missing paths excluded");
                string id = ManualSchemeRules.Save(store,topology,captured,null,"自己的方案");
                Check(store.Read().Schemes.Single().Rules.Single().Mask==8 && store.StartupSchemeId=="game", "new scheme saved without choosing or enabling it");
                store.Update(delegate(SchemeData data) { data.Schemes.Single().Rules.Add(rule(@"D:\Other\B.exe",16)); });
                ManualSchemeRules.Save(store,topology,new[] {rule(@"C:\ManualFixture\A.exe",32)},id,"");
                SavedScheme saved = new SchemeStore(Path.Combine(working,"manual-save","schemes.json")).Read().Schemes.Single();
                Check(saved.Rules.Count==2 && saved.Rules.Single(r=>r.Application.Paths.Contains(@"C:\ManualFixture\A.exe")).Mask==32 && saved.Rules.Single(r=>r.Application.Paths.Contains(@"D:\Other\B.exe")).Mask==16, "matching path replaced and unrelated rule durable");
                Check(captured[0].Mask==8 && store.StartupSchemeId=="game", "merge does not mutate source draft or startup selection");
            });
            Case("manual save: empty, missing scheme, invalid names and changed topology cannot overwrite persisted rules", delegate
            {
                string path=Path.Combine(working,"manual-save-invalid","schemes.json"); SchemeStore store=new SchemeStore(path);
                SchemeRule item=rule(@"C:\ManualFixture\A.exe",1);
                string id=ManualSchemeRules.Save(store,topology,new[]{item},null,"保留"); string before=File.ReadAllText(path);
                Throws(delegate { ManualSchemeRules.Save(store,topology,new SchemeRule[0],id,""); });
                Throws(delegate { ManualSchemeRules.Save(store,topology,new[]{item},"missing",""); });
                Throws(delegate { ManualSchemeRules.Save(store,topology,new[]{item},null,"游戏"); });
                Throws(delegate { ManualSchemeRules.Save(store,topology,new[]{item},null,"保留"); });
                Topology changed=Topology.Parse(Fixture(false)); changed.CpuName="changed";
                Throws(delegate { ManualSchemeRules.Save(store,changed,new[]{item},id,""); });
                Check(File.ReadAllText(path)==before && store.Read().Schemes.Single().Rules.Single().Mask==1,"all rejected drafts preserve disk and memory");
            });
            Case("session save: cumulative hard affinity and CPU sets remain distinct inside a grouped app", delegate
            {
                string path = Path.Combine(working, "session-save", "schemes.json");
                var store = new SchemeStore(path);
                var draft = new List<SchemeRule>();
                var a = new ProcessRow { Pid = 990001, Created = 3, Name = "A.exe", ExecutablePath = @"C:\Fixture\A.exe", Mask = 8 };
                var b = new ProcessRow { Pid = 990002, Created = 3, Name = "B.exe", ExecutablePath = @"C:\Fixture\B.exe", Mask = 0xffffffff, CpuSetMask = 16 };
                var other = new ProcessRow { Pid = 990003, Created = 3, Name = "other.exe", ExecutablePath = @"C:\Fixture\other.exe" };
                ManualSchemeRules.Merge(draft, ManualSchemeRules.Capture(new[] { a, b }, 1, delegate { return null; }, false, true));
                ManualSchemeRules.Merge(draft, ManualSchemeRules.Capture(new[] { a }, 8, delegate { return null; }, false, true));
                ManualSchemeRules.Merge(draft, ManualSchemeRules.Capture(new[] { b }, 16, delegate { return null; }, true, true));
                string id = ManualSchemeRules.Save(store, topology, draft, null, "每个进程保留自己的模式");
                SchemeData loaded = new SchemeStore(path).Read();
                Check(loaded.Version == 4 && loaded.Schemes.Single().Rules.Count == 2 && store.StartupSchemeId == "game", "each path has one last successful rule and saving does not enable it");
                var group = new ApplicationGroup(); group.Members.AddRange(new[] { a, b, other });
                var session = new AutomationSession(id, new ProcessRow[0]);
                var targets = session.Plan(new[] { group }, loaded, topology);
                Check(targets.Count == 2 && targets.Single(target => target.Process == a).Mask == 8 &&
                    targets.Single(target => target.Process == b).UseCpuSets && targets.Single(target => target.Process == b).Mask == 16,
                    "two exact paths keep different masks and unassigned siblings are excluded");
                Check(session.ExecutionStatus(targets.Single(target => target.Process == b)) == "当前已符合目标", "CPU Sets rule reports actual default CPU set");
            });
            Case("current settings: mixed instances keep common fields and updating a scheme retains unrelated rules", delegate
            {
                const string shared = @"C:\Fixture\Shared.exe", offline = @"C:\Other\Offline.exe";
                var first = new ProcessRow { Pid=990001, Created=3, Name="Shared.exe", ExecutablePath=shared, Mask=3, CpuSetMask=0, Priority=PriorityLevels.Normal };
                var second = new ProcessRow { Pid=990002, Created=3, Name="Shared.exe", ExecutablePath=shared, Mask=3, CpuSetMask=0, Priority=PriorityLevels.BelowNormal };
                int incomplete;
                List<SchemeRule> captured = ManualSchemeRules.CaptureCurrent(new[] { first, second }, topology, delegate { return null; }, out incomplete);
                Check(incomplete == 1 && captured.Single().Mask == 3 && captured.Single().CpuSets == 0 && captured.Single().CapturedPriority == 0,
                    "multiple live processes of one executable can preserve matching CPU settings without guessing a priority");
                string file = Path.Combine(working, "current-merge", "schemes.json");
                var store = new SchemeStore(file);
                var existing = rule(offline, 8);
                existing.CapturedPriority = PriorityLevels.AboveNormal;
                string id = ManualSchemeRules.Save(store, topology, new[] { existing }, null, "保留离线规则");
                ManualSchemeRules.Save(store, topology, captured, id, "");
                SchemeData data = new SchemeStore(file).Read();
                Check(data.Schemes.Single().Rules.Any(item => item.Application.Paths.Contains(offline) && item.Mask == 8) &&
                    data.PerformanceFor(id).Rules.Single().Path == offline && data.PerformanceFor(id).Rules.Single().Priority == PriorityLevels.AboveNormal,
                    "the matching running rules are updated while offline affinity and priority remain intact");
            });
            Topology actual=Topology.Read(); if(!actual.CanAssign) return;
            Case("live current settings: fresh reads without manual changes, cancel is inert, future instance receives all three settings", delegate
            {
                string folder=Path.Combine(working,"manual-save-live"); Directory.CreateDirectory(folder);
                string executable=Path.Combine(folder,"manual-unique.exe"); File.Copy(typeof(SelfTests).Assembly.Location,executable); File.Copy(typeof(SelfTests).Assembly.Location+".config",executable+".config");
                using(Process child=Process.Start(new ProcessStartInfo{FileName=executable,Arguments="--test-worker",UseShellExecute=false,CreateNoWindow=true}))
                {
                    AffinityService service=new AffinityService(actual,new SnapshotStore(Path.Combine(folder,"original.json")));
                    SchemeStore store=new SchemeStore(Path.Combine(folder,"schemes.json"));
                    ulong first=1UL<<Masks.Bits(actual.Available).First(), mask=actual.Available;
                    var cpuSets = new CpuSetService(actual, folder);
                    var priorities = new PriorityService(folder);
                    try
                    {
                        int missing; ProcessRow live=service.Processes(out missing).Single(p=>p.Pid==child.Id);
                        Check(cpuSets.Apply(live, first).Success && priorities.Apply(live, PriorityLevels.BelowNormal).Success,
                            "the worker is changed outside the form before capture");
                        using(MainForm form=new MainForm(service))
                        using(System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=30})
                        {
                            typeof(MainForm).GetField("schemeStore",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,store);
                            typeof(MainForm).GetField("cpuSetService",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,cpuSets);
                            typeof(MainForm).GetField("priorityService",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,priorities);
                            bool commit=false,visited=false;
                            timer.Tick+=delegate {
                                ManualSchemeSaveDialog dialog=Application.OpenForms.OfType<ManualSchemeSaveDialog>().FirstOrDefault(); if(dialog==null)return;
                                timer.Stop(); visited=true;
                                if(!commit)dialog.DialogResult=DialogResult.Cancel;
                                else
                                {
                                    ChildControls(dialog).OfType<TextBox>().Single().Text = "新建测试方案";
                                    ChildControls(dialog).OfType<Button>().Single(b=>b.Text=="保存方案").PerformClick();
                                }
                            };
                            form.Handle.ToInt64(); timer.Start(); StartupInvoke(form,"OpenSaveSessionScheme");
                            Check(visited && store.Read().Schemes.Count==0 && Read(child.Id)==mask,"cancel does not save or assign");
                            commit=true; visited=false; timer.Start(); StartupInvoke(form,"OpenSaveSessionScheme");
                            Check(visited && store.Read().Schemes.Count==1 && StartupField(form,"automation")==null && store.StartupSchemeId=="game" && Read(child.Id)==mask,"save leaves manual view and automation paused");
                            SchemeData data = new SchemeStore(Path.Combine(folder,"schemes.json")).Read();
                            string id = data.Schemes.Single().Id;
                            SchemeRule saved = data.Schemes.Single().Rules.Single(ruleItem => ruleItem.Application.Paths.Contains(executable));
                            Check(data.Version == 5 && saved.Mask == mask && saved.CpuSets == first &&
                                data.PerformanceFor(id).Rules.Single(item => item.Path == executable).Priority == PriorityLevels.BelowNormal,
                                "the saved file contains hard affinity, default CPU sets and priority from the fresh read");
                            using(Process later=Process.Start(new ProcessStartInfo{FileName=executable,Arguments="--test-worker",UseShellExecute=false,CreateNoWindow=true}))
                            {
                                try {
                                    ProcessRow next=service.Processes(out missing).Single(p=>p.Pid==later.Id);
                                    if (first != mask) Check(service.Apply(next, first).Success, "prepare a narrower affinity for the future instance");
                                    next=service.Processes(out missing).Single(p=>p.Pid==later.Id);
                                    AutomationSession session=new AutomationSession(id,new ProcessRow[0]);
                                    typeof(MainForm).GetField("automation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,session);
                                    var fixtureRows = new List<ProcessRow> { next };
                                    AutomaticTarget target=session.Plan(ApplicationGroup.Build(fixtureRows,delegate{return null;}),data,actual).Single();
                                    StartupInvoke(form,"ExecuteAutomaticTarget",target);
                                    StartupInvoke(form,"UpdatePerformance",data.PerformanceFor(id),fixtureRows,true);
                                    next=service.Processes(out missing).Single(p=>p.Pid==later.Id);
                                    Check(next.Mask==mask && next.CpuSetMask==first && next.Priority==PriorityLevels.BelowNormal,
                                        "a new instance receives the stored hard mask, default CPU set and priority");
                                    Check(cpuSets.Apply(next, 0, allowClear: true).Success && service.Processes(out missing).Single(p=>p.Pid==later.Id).CpuSetMask==0,
                                        "a saved zero mask can explicitly clear a default CPU set with verification");
                                    typeof(MainForm).GetField("automation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,null);
                                } finally { foreach(OriginalAffinity record in service.Store.Items.Where(r=>r.Pid==later.Id).ToArray())service.Restore(record); if(!later.HasExited){later.Kill();later.WaitForExit();} }
                            }
                        }
                    }
                    finally { foreach(OriginalAffinity record in service.Store.Items.ToArray())service.Restore(record); if(!child.HasExited){child.Kill();child.WaitForExit();} }
                }
            });
        }
    }
}
