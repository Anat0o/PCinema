using System.Windows;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;
using System.Drawing.Drawing2D;
namespace PCinema;

public sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;readonly Drawing.Icon image;readonly Forms.ContextMenuStrip menu;
    public TrayIcon(Action open,Action library,Action history,Action exit)
    {
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/PCinema;component/Assets/pcinema.ico")).Stream;
        image=new Drawing.Icon(stream);
        menu=new Forms.ContextMenuStrip{ShowImageMargin=false,ShowCheckMargin=false,BackColor=Drawing.Color.FromArgb(23,27,36),ForeColor=Drawing.Color.White,Font=new Drawing.Font("Segoe UI",11),Padding=new Forms.Padding(10),Renderer=new DarkMenuRenderer()};
        Add("Открыть PCinema",open);Add("Библиотека",library);Add("История",history);
        menu.Items.Add(new Forms.ToolStripSeparator());Add("Закрыть",exit);
        menu.Opening+=(_,_)=>
        {
            float scale=menu.DeviceDpi/96f;
            menu.Padding=new Forms.Padding((int)(12*scale));
            foreach(Forms.ToolStripItem item in menu.Items)
            {
                item.AutoSize=false;
                item.Size=new Drawing.Size((int)(240*scale),(int)((item is Forms.ToolStripSeparator?14:44)*scale));
            }
        };
        menu.SizeChanged+=(_,_)=>{using var path=DarkMenuRenderer.Round(new Drawing.Rectangle(0,0,menu.Width,menu.Height),16*menu.DeviceDpi/96f);var old=menu.Region;menu.Region=new Drawing.Region(path);old?.Dispose();};
        icon=new Forms.NotifyIcon{Icon=image,Text="PCinema",ContextMenuStrip=menu,Visible=false};icon.DoubleClick+=(_,_)=>open();
    }
    void Add(string text,Action action)
    {
        var item=new Forms.ToolStripMenuItem(text){Padding=new Forms.Padding(18,10,24,10)};
        item.Click+=(_,_)=>action();menu.Items.Add(item);
    }
    public bool Visible{get=>icon.Visible;set=>icon.Visible=value;}
    public void Dispose(){icon.Visible=false;icon.Dispose();var font=menu.Font;menu.Dispose();font.Dispose();image.Dispose();}
    sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer(){RoundedEdges=false;}
        internal static GraphicsPath Round(Drawing.Rectangle r,float radius)
        {
            var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
            p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
        }
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)=>e.Graphics.Clear(Drawing.Color.FromArgb(23,27,36));
        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e){}
        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if(!e.Item.Selected)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using var shape=Round(new Drawing.Rectangle(0,0,e.Item.Width-1,e.Item.Height-1),8*(e.ToolStrip?.DeviceDpi??96)/96f);
            using var brush=new Drawing.SolidBrush(Drawing.Color.FromArgb(43,47,56));e.Graphics.FillPath(brush,shape);
        }
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor=Drawing.Color.FromArgb(244,246,250);
            e.TextRectangle=new Drawing.Rectangle(8,0,e.Item.Width-16,e.Item.Height);
            e.TextFormat=Forms.TextFormatFlags.HorizontalCenter|Forms.TextFormatFlags.VerticalCenter|Forms.TextFormatFlags.SingleLine|Forms.TextFormatFlags.NoPrefix;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e){using var pen=new Drawing.Pen(Drawing.Color.FromArgb(55,59,68));e.Graphics.DrawLine(pen,16,e.Item.Height/2,e.Item.Width-16,e.Item.Height/2);}
    }
}
