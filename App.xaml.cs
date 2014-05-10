using System.Windows;

namespace SorceryHex {
   /// <summary>
   /// Interaction logic for App.xaml
   /// </summary>
   public partial class App : Application {
      protected override void OnStartup(StartupEventArgs e) {
         base.OnStartup(e);
         var rom = Utils.LoadRom(e.Args);
         if (rom == null) { this.Shutdown(); return; }
         var window = new MainWindow(new GbaDataFormatter(new DataHolder(rom), rom));
         window.Show();
      }
   }
}
