using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void DragGestureTests(string working)
        {
            Case("drag preview: native creation retains owner activation and mouse capture across repeated drags", delegate
            {
                using (Form owner = new Form { ShowInTaskbar=false, StartPosition=FormStartPosition.Manual, Location=new Point(-2000,-2000), Size=new Size(200,160) })
                using (Button source = new Button { Size=new Size(80,60) })
                using (Bitmap icon = new Bitmap(20,20))
                {
                    owner.Controls.Add(source); owner.Show(); owner.Activate(); source.Focus();
                    Application.DoEvents();
                    Check(owner.ContainsFocus, "native owner is active before preview creation");
                    int deactivations=0; owner.Deactivate += delegate { deactivations++; };
                    DragImagePart part = new DragImagePart { Visual=new ProcessVisual { Icon=icon }, ScreenBounds=new RectangleF(-1980,-1980,20,20), BaseBounds=new RectangleF(-1990,-1990,40,40) };
                    for (int i=0;i<3;i++)
                    {
                        source.Capture=true;
                        using (AppDragPreview preview = new AppDragPreview(new[] {part},new Point(-1980,-1980),1))
                        {
                            preview.ShowPreview(owner); Application.DoEvents();
                            Check(preview.Visible && !preview.IsDisposed && preview.Owner==owner, "visible owned preview survives native initialization");
                            Check(deactivations==0 && owner.ContainsFocus && source.Capture, "showing the ghost never activates it or cancels source capture");
                            preview.FollowPointer(); preview.Close();
                        }
                        Check(deactivations==0 && source.Capture, "preview cleanup leaves the source ready for the next drag");
                        source.Capture=false;
                    }
                    owner.Close();
                }
            });
            Case("drag gesture: previous queued mouse release cannot erase a new press", delegate
            {
                AffinityService service = new AffinityService(Topology.Parse(Fixture(false)), new SnapshotStore(Path.Combine(working,"drag-generation","original.json")));
                ApplicationGroup app = new ApplicationGroup { Title="手势测试" }; app.Members.Add(new ProcessRow { Pid=999991, Created=1, Name="gesture.exe", ExecutablePath=@"C:\GestureFixture\gesture.exe", Mask=1 });
                ApplicationIconGroup group = new ApplicationIconGroup(); group.Applications.Add(app);
                using (MainForm form = new MainForm(service))
                using (AppIconTile tile = new AppIconTile { Group=group, Size=new Size(96,96) })
                {
                    form.Handle.ToInt64(); ((AppIconPanel)StartupField(form,"applications")).Controls.Add(tile); StartupInvoke(form,"BindApplicationInteraction",tile);
                    MethodInfo down = typeof(Control).GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance);
                    down.Invoke(tile,new object[]{new MouseEventArgs(MouseButtons.Left,1,20,20,0)});
                    StartupInvoke(form,"QueueInteractionEnd");
                    down.Invoke(tile,new object[]{new MouseEventArgs(MouseButtons.Left,1,20,20,0)});
                    Application.DoEvents();
                    Check(object.ReferenceEquals(StartupField(form,"pressedTile"),tile),"new gesture survives stale cleanup");
                    StartupInvoke(form,"QueueInteractionEnd"); Application.DoEvents();
                    Check(StartupField(form,"pressedTile")==null && service.Store.Items.Length==0,"its own release still cleans up without a write");
                }
            });
            Case("drag gesture: rapid second press remains draggable and never opens CPU picker after movement", delegate
            {
                AffinityService service = new AffinityService(Topology.Parse(Fixture(false)),new SnapshotStore(Path.Combine(working,"double-drag","original.json")));
                ApplicationGroup app = new ApplicationGroup { Title="模拟拖动" }; app.Members.Add(new ProcessRow { Pid=999992, Created=1, Name="drag.exe", ExecutablePath=@"C:\GestureFixture\drag.exe", Mask=1 });
                ApplicationIconGroup group = new ApplicationIconGroup(); group.Applications.Add(app);
                using (MainForm form = new MainForm(service))
                using (AppIconTile tile = new AppIconTile { Group=group, Size=new Size(96,96) })
                using (Timer guard = new Timer { Interval=30 })
                {
                    form.Handle.ToInt64(); ((AppIconPanel)StartupField(form,"applications")).Controls.Add(tile); StartupInvoke(form,"BindApplicationInteraction",tile);
                    bool unexpectedPicker=false;
                    guard.Tick += delegate { foreach(Form f in Application.OpenForms) if(f is CpuMaskDialog) { unexpectedPicker=true; f.DialogResult=DialogResult.Cancel; break; } }; guard.Start();
                    MethodInfo down=typeof(Control).GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance);
                    MethodInfo move=typeof(Control).GetMethod("OnMouseMove",BindingFlags.NonPublic|BindingFlags.Instance);
                    MethodInfo up=typeof(AppIconTile).GetMethod("OnMouseUp",BindingFlags.NonPublic|BindingFlags.Instance);
                    down.Invoke(tile,new object[]{new MouseEventArgs(MouseButtons.Left,2,20,20,0)}); Application.DoEvents();
                    Check(!unexpectedPicker && StartupField(form,"pendingCoreEditTile")==tile && StartupField(form,"pressedTile")==tile,"no picker before second release; drag source retained");
                    move.Invoke(tile,new object[]{new MouseEventArgs(MouseButtons.Left,0,60+SystemInformation.DragSize.Width,60,0)});
                    Check(StartupField(form,"activeDrag")!=null && StartupField(form,"pendingCoreEditTile")==null,"crossing drag threshold chooses drag over double click");
                    up.Invoke(tile,new object[]{new MouseEventArgs(MouseButtons.Left,1,40,40,0)}); Application.DoEvents();
                    Check(!unexpectedPicker && StartupField(form,"activeDrag")==null && !(bool)StartupField(form,"dragCursorHidden") && service.Store.Items.Length==0,"release off a CPU target cancels cleanly with cursor restored");
                }
            });
        }
    }
}
