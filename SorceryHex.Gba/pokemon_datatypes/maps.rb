# map:
#    *mapTileData
#      width height
#        *borderTile // 8 bytes, no pointers
#       *tile[width*height] // 2 bytes: logical for 6 bits, visual for 10
#       *tileset
#       *tileset
#       .borderWidth .borderHeight ._ ._
#    *mapEventData
#       .personCount .warpCount .scriptCount .signpostCount
#       *persons[personCount]
#          .? .picture .? .?
#          -x -y
#          .? .movementType .movement .?
#          .isTrainer .? -viewRadius
#          *script
#          -id .? .?
#       *warps[warpCount] { -x -y .? .warp .map .bank }
#       *scripts[scriptCount] { -x -y -? -scriptVaribale -scriptVariableValue -? *script }
#       *signposts[signpostCount]
#          -x -y
#          .talkingLevel .signpostType -?
#          ?
#          *script // || -itemID .hiddenID .amount
#    *script
#    *connections
#       count
#       *data[count] { type offset .bank .map -_ }
#    -song -map
#    .label_id .flash .weather .type
#    -_ .labelToggle ._

#   public interface IPokemonDatatypeBuilder {
#      dynamic Result { get; }
#      byte ReadByte(string name);
#      short ReadShort(string name);
#      int ReadWord(string name);
#      dynamic ReadPointer(string name, ChildReader reader);
#      dynamic ReadNullablePointer(string name, ChildReader reader);
#      dynamic ReadArray(string name, int length, ChildReader reader);
#      dynamic ReadDynamicArray(string name, int stride, byte ender, ChildReader reader);
#   }

matchingMapLayouts = FindMany { |b|
  b.ReadPointer "mapTileData" { |b1|
    b1.ReadWord "width"
    b1.ReadWord "height"
    b1.ReadPointer "borderTile"
    b1.ReadPointer "tile"
    b1.ReadPointer "tileset1"
    b1.ReadPointer "tileset2"
    b1.ReadByte "borderWidth"
    b1.ReadByte "borderHeight"
    b1.ReadShort "_"
  }
  b.ReadPointer "mapEventData"
  b.ReadPointer "script"
  b.ReadPointer "connections"
  b.ReadShort "song"
  b.ReadShort "map"
  b.ReadByte "label_id"
  b.ReadByte "flash"
  b.ReadByte "weather"
  b.ReadByte "type"
  b.ReadShort "_"
  b.ReadByte "labelToggle"
  b.ReadByte "_"
}

# TODO do something with the matching map layouts. Find their root pointer or something.
