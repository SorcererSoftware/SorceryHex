using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SorceryHex {
   /// <summary>
   /// Interaction logic for App.xaml
   /// </summary>
   public partial class App : Application {
      [ImportMany(typeof(IModelFactory))]
      IEnumerable<IModelFactory> _factories;

      protected override void OnStartup(StartupEventArgs e) {
         base.OnStartup(e);
         string fileName;
         var contents = Utils.LoadFile(out fileName, e.Args);
         if (contents == null) { this.Shutdown(); return; }

         Compose();
         var window = new MainWindow(_factories, fileName, contents);
         window.Show();
      }

      void Compose() {
         var catalog = new AggregateCatalog();
         catalog.Catalogs.Add(new AssemblyCatalog(Assembly.GetExecutingAssembly()));
         catalog.Catalogs.Add(new DirectoryCatalog(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)));
         var subFolders = Directory.GetDirectories(AppDomain.CurrentDomain.BaseDirectory);
         foreach (var folder in subFolders) {
            catalog.Catalogs.Add(new DirectoryCatalog(folder));
         }
         CompositionContainer container = new CompositionContainer(catalog);
         container.ComposeParts(this);
      }
   }

   public interface IModelFactory : IComparable<IModelFactory> {
      string DisplayName { get; }
      bool CanCreateModel(string name, byte[] data);
      IModel CreateModel(string name, byte[] data);
   }

   [Export(typeof(IModelFactory))]
   public class SimpleFactory : IModelFactory, IModel {
      public string DisplayName { get { return "Simple"; } }
      public bool CanCreateModel(string name, byte[] data) { return true; }
      public IModel CreateModel(string name, byte[] data) { _data = data; return this; }
      public int CompareTo(IModelFactory other) { return -1; }

      byte[] _data;
      public int Length { get { return _data.Length; } }
      public void Load() { }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var list = new List<FrameworkElement>();
         while (start < 0 && length > 0) {
            list.Add(new TextBlock());
            length--;
            start++;
         }
         int extra = 0;
         while (start + length > _data.Length && length > 0) {
            extra++;
            length--;
         }
         list.AddRange(Enumerable.Range(start, length).Select(i => UseElement(Utils.ByteFlyweights[_data[i]])));
         list.AddRange(Enumerable.Range(0, extra).Select(i => new TextBlock()));
         return list;
      }

      FrameworkElement UseElement(Geometry geometry) {
         return new System.Windows.Shapes.Path {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4.0, 3.0, 4.0, 3.0),
            Data = geometry,
            Fill = Solarized.Theme.Instance.Primary
         };
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) { }
      public bool IsStartOfDataBlock(int location) { return false; }
      public bool IsWithinDataBlock(int location) { return false; }
      public int GetDataBlockStart(int location) { throw new NotImplementedException(); }
      public int GetDataBlockLength(int location) { throw new NotImplementedException(); }
      public FrameworkElement GetInterpretation(int location) { return null; }
      public IList<int> Find(string term) { return null; }

      public void Edit(int location, char c) { }
      public void CompleteEdit(int location) { }
      public event EventHandler MoveToNext;
   }

   [Export(typeof(IModelFactory))]
   public class DefaultFactory : IModelFactory {
      public string DisplayName { get { return "Default"; } }
      public bool CanCreateModel(string name, byte[] data) { return true; }
      public IModel CreateModel(string name, byte[] data) { return new CompositeModel(data); }
      public int CompareTo(IModelFactory other) { return (other is SimpleFactory) ? 1 : -1; }
   }
}
