﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SorceryHex {
   /// <summary>
   /// Interaction logic for AboutDialog.xaml
   /// </summary>
   public partial class AboutDialog : Window {
      public AboutDialog(IEnumerable<IModelFactory> factories) {
         InitializeComponent();
         Loaded += (sender, e) => icon.Animate();
         closeButton.Click += (sender, e) => Close();
         foreach (var pluginInfo in factories.Select(f => f.DisplayName + ": v. " + f.Version).Select(s => new TextBlock { Text = s })) {
            pluginList.Children.Add(pluginInfo);
         }
      }
   }
}
