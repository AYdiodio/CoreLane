using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void UnifiedViewTests(string working)
        {
            Case("single view: every running app remains visible, labels reflect read affinity without adding rules", delegate
            {
                Topology topology=Topology.Parse(Fixture(false)); string folder=Path.Combine(working,"auto-view");
                SchemeStore store=new SchemeStore(Path.Combine(folder,"schemes.json"));
                ProcessRow a=new ProcessRow{Pid=990001,Created=1,Name="A.exe",ExecutablePath=@"C:\ScopeFixture\A.exe",Mask=1};
                ProcessRow b=new ProcessRow{Pid=990002,Created=1,Name="B.exe",ExecutablePath=@"D:\ScopeFixture\B.exe",Mask=2};
                ProcessRow manual=new ProcessRow{Pid=990003,Created=1,Name="manual.exe",ExecutablePath=@"E:\ScopeFixture\manual.exe",Mask=1};
                foreach(ProcessRow row in new[]{a,b}) store.Classify(ApplicationGroup.Build(new[]{row},delegate{return null;}).Single(),row==a);
                foreach(ProcessRow row in new[]{a,b}) ManualSchemeRules.Save(store,topology,ManualSchemeRules.Capture(new[]{row},row.Mask,delegate{return null;}),null,row.Name);
                store.SelectScheme("game"); string before=File.ReadAllText(Path.Combine(folder,"schemes.json"));
                using(MainForm form=new MainForm(new AffinityService(topology,new SnapshotStore(Path.Combine(folder,"original.json")))))
                {
                    typeof(MainForm).GetField("processes",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,new List<ProcessRow>{a,b,manual});
                    typeof(MainForm).GetField("schemeStore",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,store);
                    AppIconPanel panel=(AppIconPanel)StartupField(form,"applications");
                    Func<int[]> visible=()=>panel.Controls.OfType<AppIconTile>().SelectMany(t=>t.Group.Members).Select(p=>p.Pid).OrderBy(x=>x).ToArray();
                    StartupInvoke(form,"RenderProcesses");
                    Check(visible().SequenceEqual(new[]{a.Pid,b.Pid,manual.Pid}),"classified and manual-only apps all appear regardless of scheme membership");
                    a.Mask=b.Mask=manual.Mask=0xffff; StartupInvoke(form,"RenderProcesses"); Check(panel.Controls.OfType<AppIconTile>().Single().AssignmentLabel.Text=="CPU 组 1","merged icon label follows actual masks despite selected scheme targeting CPU 1 only");
                    manual.ReadError="访问被拒绝";StartupInvoke(form,"RenderProcesses");Check(panel.Controls.OfType<AppIconTile>().Single().AssignmentLabel.Text=="部分无法读取","read failure never displays planned target as success");manual.ReadError=null;
                    Check(visible().Length==3 && StartupField(form,"automation")==null,"refresh does not start a scheme");
                    Check(File.ReadAllText(Path.Combine(folder,"schemes.json"))==before,"displaying apps changes no rules or classifications");
                }
            });
            Case("live single view: refreshes actual affinity without saving or applying rules, waits for open dialogs", delegate
            {
                Topology topology=Topology.Read(); if(!topology.CanAssign || Masks.Bits(topology.Available).Count()<2)return;
                string folder=Path.Combine(working,"paused-view");Directory.CreateDirectory(folder);string executable=Path.Combine(folder,"display-worker.exe");
                File.Copy(typeof(SelfTests).Assembly.Location,executable);File.Copy(typeof(SelfTests).Assembly.Location+".config",executable+".config");
                using(Process child=Process.Start(new ProcessStartInfo{FileName=executable,Arguments="--test-worker",UseShellExecute=false,CreateNoWindow=true}))
                {
                    SchemeStore store=new SchemeStore(Path.Combine(folder,"schemes.json"));store.SelectScheme("game");string before=File.ReadAllText(Path.Combine(folder,"schemes.json"));
                    AffinityService service=new AffinityService(topology,new SnapshotStore(Path.Combine(folder,"original.json")));ulong original=Read(child.Id);
                    try
                    {
                        int missing;ProcessRow row=service.Processes(out missing).Single(r=>r.Pid==child.Id);ulong first=1UL<<Masks.Bits(topology.Available).First(),second=1UL<<Masks.Bits(topology.Available).Skip(1).First();
                        using(MainForm form=new MainForm(service))
                        {
                            form.Handle.ToInt64();
                            typeof(MainForm).GetField("schemeStore",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,store);
                            ((TextBox)StartupField(form,"search")).Text="display-worker";
                            Check(service.Apply(row,first).Success,"test harness changes only own worker");
                            string records=File.ReadAllText(Path.Combine(folder,"original.json"));StartupInvoke(form,"AutomationTick");
                            CompleteProcessRefresh(form);
                            var read=(List<ProcessRow>)StartupField(form,"processes");Check(read.Single(p=>p.Pid==child.Id).Mask==first && StartupField(form,"automation")==null,"paused auto timer reads current affinity without starting automation");
                            AppIconPanel panel=(AppIconPanel)StartupField(form,"applications");Check(panel.Controls.OfType<AppIconTile>().SelectMany(t=>t.Group.Members).Any(p=>p.Pid==child.Id),"unclassified worker is displayed");
                            typeof(MainForm).GetField("settingsDialogOpen",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
                            Check(service.Apply(row,second).Success,"test harness simulates another current allocation");StartupInvoke(form,"AutomationTick");
                            Check(((List<ProcessRow>)StartupField(form,"processes")).Single(p=>p.Pid==child.Id).Mask==first,"modal interaction defers refresh");
                            typeof(MainForm).GetField("settingsDialogOpen",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);StartupInvoke(form,"AutomationTick");
                            CompleteProcessRefresh(form);
                            Check(((List<ProcessRow>)StartupField(form,"processes")).Single(p=>p.Pid==child.Id).Mask==second,"refresh resumes after dialog closes");
                            Check(File.ReadAllText(Path.Combine(folder,"original.json"))==records && File.ReadAllText(Path.Combine(folder,"schemes.json"))==before && store.Read().Games.Count==0 && store.Read().Schemes.Count==0 && store.Read().GameOverrides==null,"observer refresh creates no rules, classifications or recovery writes");
                        }
                        foreach(var record in service.Store.Items.ToArray())Check(service.Restore(record).Success,"restore own worker");Check(Read(child.Id)==original,"own worker exact original restored");
                    }
                    finally{foreach(var record in service.Store.Items.ToArray())service.Restore(record);if(!child.HasExited){child.Kill();child.WaitForExit();}}
                }
            });
            Case("unified dialogs: confirmation return values and manager draft editing keep original save semantics", delegate
            {
                foreach(DialogResult expected in new[]{DialogResult.Yes,DialogResult.No,DialogResult.Cancel})
                    using(AppMessageDialog dialog=new AppMessageDialog("退出或取消的说明","退出与恢复",MessageBoxButtons.YesNoCancel))
                    {
                        string text=expected==DialogResult.Yes?"是":expected==DialogResult.No?"否":"取消";
                        Button button=ChildControls(dialog).OfType<Button>().Single(b=>b.Text==text);
                        typeof(Button).GetMethod("OnClick",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(button,new object[]{EventArgs.Empty});
                        Check(dialog.DialogResult==expected,"confirmation preserves exact result "+expected);
                    }
                Topology topology=Topology.Parse(Fixture(false)); SchemeStore store=new SchemeStore(Path.Combine(working,"styled-manager","schemes.json"));
                ApplicationGroup app=SortingApp(999990,"自己的应用",@"C:\ManagerFixture\A.exe",null); app.Members[0].Mask=8;
                using(SchemeManagerDialog dialog=new SchemeManagerDialog(store,topology,new[]{app},8))
                {
                    BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                    DataGridView rules=(DataGridView)typeof(SchemeManagerDialog).GetField("rules",flags).GetValue(dialog);
                    ((TextBox)typeof(SchemeManagerDialog).GetField("name",flags).GetValue(dialog)).Text="界面测试方案";
                    rules.Rows[0].Cells["Use"].Value=true;
                    Check((bool)typeof(SchemeManagerDialog).GetMethod("SaveScheme",flags).Invoke(dialog,null),"manager still saves explicit draft");
                    Check(store.Read().Schemes.Single().Rules.Single().Mask==8 && dialog.ActivateId==null,"saving custom core rule does not enable it");
                    DataGridView games=(DataGridView)typeof(SchemeManagerDialog).GetField("games",flags).GetValue(dialog);
                    games.Rows[0].Cells["Class"].Value="非游戏";
                    Check((bool)typeof(SchemeManagerDialog).GetMethod("SaveClassifications",flags).Invoke(dialog,null) && !store.Read().Games.Single().IsGame,"classification edit still saves independently");
                }
            });
        }
        private static void CompleteProcessRefresh(MainForm form)
        {
            var elapsed = Stopwatch.StartNew();
            while ((bool)StartupField(form, "refreshInProgress") && elapsed.ElapsedMilliseconds < 5000)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
            Check(!(bool)StartupField(form, "refreshInProgress"), "background process read completes and dispatches to the owner");
        }
    }
}
