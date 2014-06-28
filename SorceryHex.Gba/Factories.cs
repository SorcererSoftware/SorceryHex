using System.ComponentModel.Composition;

namespace SorceryHex.Gba {
   namespace Pokemon { 
      [Export(typeof(IModelFactory))]
      public class Factory : IModelFactory {
         public string DisplayName { get { return "Pokemon Gba Game"; } }

         public bool CanCreateModel(string name, byte[] data) {
            if (!name.ToLower().EndsWith("gba")) return false;
            var code = Header.GetCode(data);

            //           ruby            sapphire          emerald
            if (code == "AXVE" || code == "AXPE" || code == "BPEE") return true;

            //          firered          leafgreen
            if (code == "BPRE" || code == "BPGE") return true;

            return false;
         }

         public IModel CreateModel(string name, byte[] data, ScriptInfo scriptInfo) {
            var pointerMapper = new PointerMapper(data);
            // var maps = new Maps(pointerMapper);
            var storage = new RunStorage(data
               , new Header(pointerMapper)
               , new Thumbnails(pointerMapper)
               , new Lz(pointerMapper)
               , new Pokemon.DataTypes.ScriptedDataTypes(pointerMapper, scriptInfo.Engine, scriptInfo.Scope)
               , new PCS()
            );
            IModel model = new CompositeModel(data, storage);
            model = new PointerParser(model, data, storage, pointerMapper);
            return model;
         }

         public int CompareTo(IModelFactory other) {
            if (other is StandardFactory) return 1;
            if (other is DefaultFactory) return 1;
            if (other is SimpleFactory) return 1;
            return 0;
         }
      }
   }

   [Export(typeof(IModelFactory))]
   public class StandardFactory : IModelFactory {
      public string DisplayName { get { return "Gba Game"; } }

      public bool CanCreateModel(string name, byte[] data) {
         return name.ToLower().EndsWith("gba");
      }

      public IModel CreateModel(string name, byte[] data, ScriptInfo scriptInfo) {
         var pointerMapper = new Gba.PointerMapper(data);
         var storage = new RunStorage(data
            , new Gba.Header(pointerMapper)
            , new Gba.Lz(pointerMapper)
         );
         IModel model = new CompositeModel(data, storage);
         model = new Gba.PointerParser(model, data, storage, pointerMapper);
         return model;
      }

      public int CompareTo(IModelFactory other) {
         if (other is Pokemon.Factory) return -1;
         if (other is DefaultFactory) return 1;
         if (other is SimpleFactory) return 1;
         return 0;
      }
   }
}
