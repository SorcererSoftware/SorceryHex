using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SorceryHex {
   public class AutoTimer : IDisposable {
      static readonly IDictionary<string, AutoTimer> _timers = new Dictionary<string, AutoTimer>();
      public static IEnumerable<string> Report {
         get {
            return _timers.Select(pair => pair.Key + ": " + pair.Value._average);
         }
      }
      public static void ClearReport() { _timers.Clear(); }
      public static AutoTimer Time(string key) {
         var timer = Get(key);
         Debug.Assert(!timer._watch.IsRunning);
         timer._watch.Reset();
         timer._watch.Start();
         return timer;
      }
      static AutoTimer Get(string key) {
         if (_timers.ContainsKey(key)) return _timers[key];
         var timer = new AutoTimer();
         _timers[key] = timer;
         return timer;
      }

      readonly Stopwatch _watch = new Stopwatch();
      int _runs;
      double _average;
      AutoTimer() { }

      public void Dispose() {
         _watch.Stop();
         double total = _watch.ElapsedMilliseconds + _average * _runs;
         _runs++;
         _average = total / _runs;
      }
   }
}
