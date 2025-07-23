using Gtk;

namespace ConsoleApp2;

public class Interface
{
    private Window window;
    Button button;
    
    public Interface()
    {
        Application.Init();
        window = new Window("Bash Script manager");
        window.SetDefaultSize(1300, 800);
        
        Gdk.Geometry hints = new Gdk.Geometry();
        hints.MinWidth = 900;
        hints.MinHeight = 600;
        window.SetGeometryHints(window, hints, Gdk.WindowHints.MinSize);
        
        Fixed container = new Fixed();
        
        ScrolledWindow scrolledWindow = new ScrolledWindow();
        TextView textView = new TextView();

        textView.WrapMode = WrapMode.Word;
        textView.SetSizeRequest(300, 150);
        
        scrolledWindow.Add(textView);
        scrolledWindow.SetSizeRequest(800, 250);
        container.Put(scrolledWindow, 0, 20);
        
        button = new Button("Add bash script");
        button.SetSizeRequest(15, 15);
        container.Put(button, 20, 300);
        
        window.Add(container);      
        window.ShowAll();
    }

    public void Run()
    {
        window.DeleteEvent += (o, e) => { Application.Quit();};
        window.Show();
        Application.Run();
    }


}