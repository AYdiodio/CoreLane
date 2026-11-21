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
        private static void GameSchemeRuleTests(string working)
        {
            Case("game rules: legacy migration, many independent schemes, explicit overrides precede classifications without reclassifying", delegate
            {
                Topology topology=Topology.Parse(Fixture(false)); string path=Path.Combine(working,"game-priority.json");
                File.WriteAllText(path,"{\"Version\":1,\"Games\":[],\"Schemes\":[]}"); SchemeStore store=new SchemeStore(path);
                ApplicationGroup a=SortingApp(991001,"A",@"C:\GameRuleFixture\A.exe",null), b=SortingApp(991002,"B",@"D:\GameRuleFixture\B.exe",null), c=SortingApp(991003,"C",@"E:\GameRuleFixture\C.exe",null);
                ApplicationGroup fallback=SortingApp(991004,"default",@"F:\GameRuleFixture\default.exe",null);
                store.Classify(a,true);store.Classify(b,false);store.Classify(fallback,true);
                string custom=ManualSchemeRules.Save(store,topology,ManualSchemeRules.Capture(a.Members,2,delegate{return null;}),null,"直播");
                ManualSchemeRules.Save(store,topology,ManualSchemeRules.Capture(b.Members,4,delegate{return null;}),null,"工作");
                foreach(var pair in new[]{new{App=a,Mask=8UL},new{App=b,Mask=32UL},new{App=c,Mask=16UL}})
                    ManualSchemeRules.Save(store,topology,ManualSchemeRules.Capture(pair.App.Members,pair.Mask,delegate{return null;}),"game","");
                SchemeData data=new SchemeStore(path).Read();
                Check(data.Version==2 && data.Schemes.Count==2 && data.GameOverrides.Rules.Count==3 && data.Games.Count==3,"legacy schema upgraded only after explicit game save; separate schemes and classifications retained");
                Check(data.Games.Single(g=>g.Application.Matches(a)).IsGame && !data.Games.Single(g=>g.Application.Matches(b)).IsGame && !data.Games.Any(g=>g.Application.Matches(c)),"game, non-game and unknown labels unchanged");
                AutomationSession session=new AutomationSession("game",new ProcessRow[0]); var targets=session.Plan(new[]{a,b,c,fallback},data,topology);
                Check(targets.Single(t=>t.Process.Pid==a.Members[0].Pid).Mask==8 && targets.Single(t=>t.Process.Pid==b.Members[0].Pid).Mask==32 && targets.Single(t=>t.Process.Pid==c.Members[0].Pid).Mask==16 && targets.Single(t=>t.Process.Pid==fallback.Members[0].Pid).Mask==0xffff,"manual masks win; default game rule still works for other apps");
                Check(session.NextQuestion(new[]{c},data,delegate{return true;})==null && data.GameOverrides.Rules.Any(r=>r.Application.Matches(c)),"uncategorized override is saved without another classification question");
                Check(new AutomationSession(custom,new ProcessRow[0]).Plan(new[]{a,b,c},data,topology).Single().Mask==2,"another scheme uses its own mask");
                foreach(var target in targets)session.MarkAttempted(target);Check(session.Plan(new[]{a,b,c,fallback},data,topology).Count==0,"attempt-once policy preserved");
                ApplicationGroup host=SortingApp(991006,"明确手动选择的组件",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","explicit-fixture.exe"),null);
                Check(new AutomationSession("game",new ProcessRow[0]).Plan(new[]{host},data,topology).Count==0,"unlisted system component stays excluded");
                ManualSchemeRules.Save(store,topology,ManualSchemeRules.Capture(host.Members,64,delegate{return null;}),"game","");
                Check(new AutomationSession("game",new ProcessRow[0]).Plan(new[]{host},store.Read(),topology).Single().Mask==64,"only explicitly saved system path is eligible; pure fixture never touches system process");
                string before=File.ReadAllText(path); Topology changed=Topology.Parse(Fixture(false));changed.CpuName="changed";
                Throws(delegate{session.Plan(new[]{a},data,changed);});Throws(delegate{ManualSchemeRules.Save(store,changed,data.GameOverrides.Rules,"game","");});
                Throws(delegate{store.Update(d=>d.GameOverrides.Rules.Add(d.GameOverrides.Rules[0]));});
                Check(File.ReadAllText(path)==before,"invalid topology and duplicate rules never replace saved data");
                store.DeleteScheme("game");Check(store.Read().GameOverrides==null && store.Read().Games.Count==3 && store.Read().Schemes.Count==2,"clearing game manual rules preserves every other scheme and classification");
            });
            Case("game rule dialog: batch game destination hides new-name input; cancel saves nothing; manager can edit saved game core rules", delegate
            {
                Topology topology=Topology.Parse(Fixture(false));SchemeStore store=new SchemeStore(Path.Combine(working,"game-dialog","schemes.json"));
                ApplicationGroup app=SortingApp(991005,"示例应用",@"C:\GameDialog\app.exe",null);List<SchemeRule> draft=ManualSchemeRules.Capture(app.Members,8,delegate{return null;});
                BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                using(ManualSchemeSaveDialog cancel=new ManualSchemeSaveDialog(store,topology,draft))
                {
                    var picker=(CapsuleSchemePicker)typeof(ManualSchemeSaveDialog).GetField("destination",flags).GetValue(cancel);
                    picker.SelectedIndex=picker.Items.FindIndex(x=>x.ToString()=="游戏");
                    typeof(Button).GetMethod("OnClick",flags).Invoke(cancel.CancelButton,new object[]{EventArgs.Empty});Check(store.Read().GameOverrides==null,"cancel cannot join game scheme");
                }
                using(ManualSchemeSaveDialog save=new ManualSchemeSaveDialog(store,topology,draft))
                {
                    var picker=(CapsuleSchemePicker)typeof(ManualSchemeSaveDialog).GetField("destination",flags).GetValue(save);picker.SelectedIndex=picker.Items.FindIndex(x=>x.ToString()=="游戏");
                    Check(((TableLayoutPanel)save.Controls[0]).RowStyles[3].Height==0,"existing destination removes new-name row");
                    typeof(Button).GetMethod("OnClick",flags).Invoke(ChildControls(save).OfType<Button>().Single(b=>b.Text=="保存到方案"),new object[]{EventArgs.Empty});
                    Check(save.SavedId=="game" && store.Read().GameOverrides.Rules.Single().Mask==8 && store.Read().Games.Count==0,"explicit save preserves core mask without classifying");
                }
                using(SchemeManagerDialog manager=new SchemeManagerDialog(store,topology,new[]{app},0))
                {
                    var picker=(CapsuleSchemePicker)typeof(SchemeManagerDialog).GetField("schemes",flags).GetValue(manager);picker.SelectedIndex=picker.Items.FindIndex(x=>x.ToString()=="游戏");
                    var grid=(DataGridView)typeof(SchemeManagerDialog).GetField("rules",flags).GetValue(manager);
                    Check(Convert.ToBoolean(grid.Rows[0].Cells["Use"].Value),"game manual rule loaded for editing");
                    grid.Rows[0].Cells["Use"].Value=false;
                    Check((bool)typeof(SchemeManagerDialog).GetMethod("SaveScheme",flags).Invoke(manager,null) && store.Read().GameOverrides.Rules.Count==0 && manager.ActivateId==null,"removing draft game rule saves without activation");
                }
            });
            Case("live game override: saved single CPU applies to own new process and restores original", delegate
            {
                Topology topology=Topology.Read();if(!topology.CanAssign)return;
                string folder=Path.Combine(working,"game-override-live");Directory.CreateDirectory(folder);
                string executable=Path.Combine(folder,"game-rule-worker.exe");File.Copy(typeof(SelfTests).Assembly.Location,executable);File.Copy(typeof(SelfTests).Assembly.Location+".config",executable+".config");
                using(Process child=Process.Start(new ProcessStartInfo{FileName=executable,Arguments="--test-worker",UseShellExecute=false,CreateNoWindow=true}))
                {
                    AffinityService service=new AffinityService(topology,new SnapshotStore(Path.Combine(folder,"original.json")));SchemeStore store=new SchemeStore(Path.Combine(folder,"schemes.json"));ulong before=Read(child.Id);
                    try{
                        int missing;var row=service.Processes(out missing).Single(p=>p.Pid==child.Id);var app=ApplicationGroup.Build(new[]{row},delegate{return null;}).Single();store.Classify(app,true);
                        ulong mask=1UL<<Masks.Bits(topology.Available).First();ManualSchemeRules.Save(store,topology,ManualSchemeRules.Capture(new[]{row},mask,delegate{return null;}),"game","");
                        Check(Read(child.Id)==before && service.Store.Items.Length==0,"save itself never assigns");
                        var target=new AutomationSession("game",new ProcessRow[0]).Plan(new[]{app},new SchemeStore(Path.Combine(folder,"schemes.json")).Read(),topology).Single();
                        Check(target.Mask==mask && service.Apply(target.Process,target.Mask).Success && Read(child.Id)==mask,"explicit game rule executes through normal permission backend");
                        foreach(var record in service.Store.Items.ToArray())Check(service.Restore(record).Success,"restore succeeds");Check(Read(child.Id)==before,"exact original restored");
                    }finally{foreach(var record in service.Store.Items.ToArray())service.Restore(record);if(!child.HasExited){child.Kill();child.WaitForExit();}}
                }
            });
        }
    }
}
