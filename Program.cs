using System.Text.Json;
using System.Drawing;
using System.Windows.Forms;

namespace Assistente;

internal static class Program
{
    [STAThread]
    static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new MainForm()); }
}

public enum Urgency { Baixa, Normal, Alta, Crítica }

public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Category { get; set; } = "Geral";
    public Urgency Urgency { get; set; } = Urgency.Normal;
    public DateTime? Deadline { get; set; }
    public DateTime? Reminder { get; set; }
    public bool Completed { get; set; }
    public bool ReminderSent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class MainForm : Form
{
    readonly List<TaskItem> items = [];
    readonly string dataFile;
    readonly NotifyIcon tray = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30000 };
    readonly ListView list = new();
    readonly ComboBox cat = new();
    readonly ComboBox urg = new();
    readonly Label summary = new();

    static readonly Color Bg = Color.FromArgb(248,249,251);
    static readonly Color Ink = Color.FromArgb(35,39,47);

    public MainForm()
    {
        Text = "Assistente"; Width=1050; Height=680; MinimumSize=new Size(850,560);
        StartPosition=FormStartPosition.CenterScreen; BackColor=Bg;
        Font=new Font("Segoe UI",10); Icon=SystemIcons.Application;

        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Assistente");
        Directory.CreateDirectory(dir); dataFile=Path.Combine(dir,"tarefas.json");
        LoadData(); BuildUi();

        tray.Icon=SystemIcons.Application; tray.Text="Assistente"; tray.Visible=true;
        tray.DoubleClick += (_,_)=>Restore();
        var menu=new ContextMenuStrip();
        menu.Items.Add("Abrir Assistente",null,(_,_)=>Restore());
        menu.Items.Add("Nova demanda",null,(_,_)=>Edit(null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair",null,(_,_)=>{tray.Visible=false; Application.Exit();});
        tray.ContextMenuStrip=menu;

        timer.Tick += (_,_)=>CheckReminders(); timer.Start();
        Shown += (_,_)=>CheckReminders();
        FormClosing += (_,e)=>{ if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();tray.ShowBalloonTip(1800,"Assistente","O aplicativo continua em segundo plano para os lembretes.",ToolTipIcon.Info);} };
    }

    void BuildUi()
    {
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(28,24,28,22),BackColor=Bg};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,70)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Controls.Add(root);

        var header=new Panel{Dock=DockStyle.Fill};
        header.Controls.Add(new Label{Text="Assistente",AutoSize=true,Font=new Font("Segoe UI Semibold",25),ForeColor=Ink,Location=new Point(0,0)});
        header.Controls.Add(new Label{Text="Suas demandas, organizadas de forma simples.",AutoSize=true,ForeColor=Color.FromArgb(105,112,122),Location=new Point(2,40)});
        var add=Button("+  Nova demanda",true); add.Size=new Size(175,42); add.Anchor=AnchorStyles.Top|AnchorStyles.Right; add.Location=new Point(header.Width-add.Width,7);
        header.Resize+=(_,_)=>add.Left=header.ClientSize.Width-add.Width; add.Click+=(_,_)=>Edit(null); header.Controls.Add(add); root.Controls.Add(header);

        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,5,0,0)};
        cat.DropDownStyle=ComboBoxStyle.DropDownList; cat.Width=190; cat.Items.Add("Todas as categorias"); cat.SelectedIndex=0; cat.SelectedIndexChanged+=(_,_)=>RefreshList();
        urg.DropDownStyle=ComboBoxStyle.DropDownList; urg.Width=160; urg.Items.Add("Todas as urgências"); foreach(var x in Enum.GetValues<Urgency>()) urg.Items.Add(x); urg.SelectedIndex=0; urg.SelectedIndexChanged+=(_,_)=>RefreshList();
        filters.Controls.Add(cat); filters.Controls.Add(urg); root.Controls.Add(filters);

        summary.Dock=DockStyle.Fill; summary.ForeColor=Color.FromArgb(105,112,122); summary.TextAlign=ContentAlignment.MiddleLeft; root.Controls.Add(summary);

        list.Dock=DockStyle.Fill; list.View=View.Details; list.FullRowSelect=true; list.HideSelection=false; list.BorderStyle=BorderStyle.None; list.BackColor=Color.White; list.ForeColor=Ink;
        foreach(var c in new[]{("Demanda",360),("Categoria",150),("Urgência",110),("Prazo",125),("Lembrete",145),("Status",100)}) list.Columns.Add(c.Item1,c.Item2);
        list.DoubleClick+=(_,_)=>{if(list.SelectedItems.Count>0)Edit((Guid)list.SelectedItems[0].Tag!);};
        list.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Delete)DeleteSelected(); if(e.KeyCode==Keys.Enter&&list.SelectedItems.Count>0)Edit((Guid)list.SelectedItems[0].Tag!);};
        root.Controls.Add(list); RefreshCategories(); RefreshList();
    }

    static Button Button(string text,bool primary)=>new(){Text=text,Height=36,Width=125,FlatStyle=FlatStyle.Flat,BackColor=primary?Color.FromArgb(31,92,190):Color.White,ForeColor=primary?Color.White:Ink,TextAlign=ContentAlignment.MiddleCenter,Margin=new Padding(0,0,10,0)};

    void Edit(Guid? id)
    {
        var old=id.HasValue?items.FirstOrDefault(x=>x.Id==id.Value):null;
        using var d=new Editor(old);
        if(d.ShowDialog(this)!=DialogResult.OK||d.Result==null)return;
        if(old==null)items.Add(d.Result);else items[items.IndexOf(old)]=d.Result;
        Save(); RefreshCategories(); RefreshList();
    }

    void DeleteSelected()
    {
        if(list.SelectedItems.Count==0)return;
        var id=(Guid)list.SelectedItems[0].Tag!; var x=items.FirstOrDefault(a=>a.Id==id); if(x==null)return;
        if(MessageBox.Show($"Excluir “{x.Title}”?","Confirmar",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){items.Remove(x);Save();RefreshList();}
    }

    void RefreshCategories()
    {
        var current=cat.SelectedItem?.ToString(); cat.Items.Clear(); cat.Items.Add("Todas as categorias");
        foreach(var x in items.Select(a=>a.Category).Where(a=>!string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a=>a))cat.Items.Add(x);
        var i=current==null?0:cat.Items.IndexOf(current);cat.SelectedIndex=i>=0?i:0;
    }

    void RefreshList()
    {
        list.BeginUpdate();list.Items.Clear();
        IEnumerable<TaskItem> q=items.OrderBy(x=>x.Completed).ThenBy(x=>x.Deadline??DateTime.MaxValue).ThenByDescending(x=>x.Urgency);
        var c=cat.SelectedItem?.ToString();var u=urg.SelectedItem?.ToString();
        if(c!="Todas as categorias"&&!string.IsNullOrWhiteSpace(c))q=q.Where(x=>x.Category.Equals(c,StringComparison.OrdinalIgnoreCase));
        if(u!="Todas as urgências"&&Enum.TryParse<Urgency>(u,out var uv))q=q.Where(x=>x.Urgency==uv);
        foreach(var x in q){
            var r=new ListViewItem(x.Title);r.SubItems.Add(x.Category);r.SubItems.Add(x.Urgency.ToString());r.SubItems.Add(x.Deadline?.ToString("dd/MM/yyyy HH:mm")??"—");r.SubItems.Add(x.Reminder?.ToString("dd/MM/yyyy HH:mm")??"—");r.SubItems.Add(x.Completed?"Concluída":"Pendente");r.Tag=x.Id;
            if(x.Completed)r.ForeColor=Color.FromArgb(145,150,158);else if(x.Deadline<xNow())r.ForeColor=Color.FromArgb(190,60,60);
            if(x.Urgency==Urgency.Crítica)r.BackColor=Color.FromArgb(255,242,242);else if(x.Urgency==Urgency.Alta)r.BackColor=Color.FromArgb(255,248,232);
            list.Items.Add(r);
        }
        list.EndUpdate();
        summary.Text=$"{items.Count(x=>!x.Completed)} pendente(s)  •  {items.Count(x=>!x.Completed&&x.Deadline<xNow())} atrasada(s)  •  {items.Count(x=>x.Completed)} concluída(s)";
    }

    static DateTime xNow()=>DateTime.Now;

    void CheckReminders()
    {
        var changed=false;
        foreach(var x in items.Where(a=>!a.Completed&&!a.ReminderSent&&a.Reminder.HasValue&&a.Reminder.Value<=DateTime.Now)){
            tray.ShowBalloonTip(7000,"Lembrete — Assistente",x.Title,ToolTipIcon.Info);x.ReminderSent=true;changed=true;
        }
        if(changed){Save();RefreshList();}
    }

    void Restore(){Show();WindowState=FormWindowState.Normal;Activate();}

    void LoadData(){try{if(File.Exists(dataFile)){var x=JsonSerializer.Deserialize<List<TaskItem>>(File.ReadAllText(dataFile));if(x!=null)items.AddRange(x);}}catch{}}
    void Save()=>File.WriteAllText(dataFile,JsonSerializer.Serialize(items,new JsonSerializerOptions{WriteIndented=true}));
}

public sealed class Editor:Form
{
    readonly TextBox title=new(), category=new(); readonly ComboBox urgency=new(); readonly CheckBox hasDeadline=new(),hasReminder=new(),done=new();
    readonly DateTimePicker deadline=new(),reminder=new(); readonly TaskItem? old;
    public TaskItem? Result{get;private set;}

    public Editor(TaskItem? item)
    {
        old=item;Text=item==null?"Nova demanda":"Editar demanda";Width=520;Height=475;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(28),ColumnCount=2,RowCount=8};p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=0;i<8;i++)p.RowStyles.Add(new RowStyle(SizeType.Absolute,43));Controls.Add(p);
        Add(p,0,"Demanda",title);Add(p,1,"Categoria",category);
        urgency.DropDownStyle=ComboBoxStyle.DropDownList;foreach(var u in Enum.GetValues<Urgency>())urgency.Items.Add(u);Add(p,2,"Urgência",urgency);
        hasDeadline.Text="Definir prazo";hasDeadline.AutoSize=true;hasDeadline.CheckedChanged+=(_,_)=>deadline.Enabled=hasDeadline.Checked;deadline.Format=DateTimePickerFormat.Custom;deadline.CustomFormat="dd/MM/yyyy  HH:mm";deadline.ShowUpDown=true;deadline.Enabled=false;
        var dp=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};dp.Controls.Add(hasDeadline);dp.Controls.Add(deadline);Add(p,3,"Prazo",dp);
        hasReminder.Text="Criar lembrete";hasReminder.AutoSize=true;hasReminder.CheckedChanged+=(_,_)=>reminder.Enabled=hasReminder.Checked;reminder.Format=DateTimePickerFormat.Custom;reminder.CustomFormat="dd/MM/yyyy  HH:mm";reminder.ShowUpDown=true;reminder.Enabled=false;
        var rp=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};rp.Controls.Add(hasReminder);rp.Controls.Add(reminder);Add(p,4,"Lembrete",rp);
        done.Text="Marcar como concluída";done.AutoSize=true;Add(p,5,"Status",done);
        p.Controls.Add(new Label{Text="Categoria é livre: trabalho, casa, compras, pessoal, projeto…",ForeColor=Color.FromArgb(105,112,122),AutoSize=true,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},1,6);
        var bp=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var save=new Button{Text="Salvar",Width=105,Height=34,BackColor=Color.FromArgb(31,92,190),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};var cancel=new Button{Text="Cancelar",Width=105,Height=34,DialogResult=DialogResult.Cancel};save.Click+=(_,_)=>SaveResult();bp.Controls.Add(save);bp.Controls.Add(cancel);p.Controls.Add(bp,1,7);AcceptButton=save;CancelButton=cancel;
        if(item!=null){title.Text=item.Title;category.Text=item.Category;urgency.SelectedItem=item.Urgency;done.Checked=item.Completed;if(item.Deadline.HasValue){hasDeadline.Checked=true;deadline.Value=Clamp(item.Deadline.Value);}if(item.Reminder.HasValue){hasReminder.Checked=true;reminder.Value=Clamp(item.Reminder.Value);}}
        else{category.Text="Geral";urgency.SelectedItem=Urgency.Normal;deadline.Value=DateTime.Now.AddDays(1);reminder.Value=DateTime.Now.AddHours(1);}
    }

    static void Add(TableLayoutPanel p,int row,string label,Control c){p.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(75,80,88)},0,row);c.Dock=DockStyle.Fill;p.Controls.Add(c,1,row);}
    static DateTime Clamp(DateTime d)=>d<DateTimePicker.MinimumDateTime?DateTimePicker.MinimumDateTime:d>DateTimePicker.MaximumDateTime?DateTimePicker.MaximumDateTime:d;

    void SaveResult()
    {
        if(string.IsNullOrWhiteSpace(title.Text)){MessageBox.Show("Informe a demanda.","Atenção",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
        var sameReminder=old?.Reminder.HasValue==true&&hasReminder.Checked&&old.Reminder!.Value==reminder.Value;
        Result=new TaskItem{Id=old?.Id??Guid.NewGuid(),Title=title.Text.Trim(),Category=string.IsNullOrWhiteSpace(category.Text)?"Geral":category.Text.Trim(),Urgency=urgency.SelectedItem is Urgency u?u:Urgency.Normal,Deadline=hasDeadline.Checked?deadline.Value:null,Reminder=hasReminder.Checked?reminder.Value:null,Completed=done.Checked,CreatedAt=old?.CreatedAt??DateTime.Now,ReminderSent=sameReminder&&(old?.ReminderSent??false)};
        DialogResult=DialogResult.OK;
    }
}
