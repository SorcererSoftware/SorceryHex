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
         IElementFactory factory = new DataHolder(rom);
         factory = new GbaHeaderFormatter(factory, rom);
         factory = GbaLzFormatterFactory.Images(factory, rom);
         factory = GbaLzFormatterFactory.Palette(factory, rom);
         factory = new GbaPointerFormatter(factory, rom);
         factory = new RangeChecker(factory);
         var window = new MainWindow(factory);
         window.Show();
      }
   }
}
