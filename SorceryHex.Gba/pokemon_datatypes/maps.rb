maps = types.FindMany ->(b){
   b.ReadPointer "mapTileData", ->(b){
      b.ReadWord "width" 
      b.ReadWord "height" 
      b.ReadPointer "borderTile" 
      b.ReadPointer "tiles" 
      b.ReadPointer "tileset1" 
      b.ReadPointer "tileset2" 
      b.ReadByte "borderWidth" # TODO only if FR / LG
      b.ReadByte "borderHeight"
      b.ReadShort "_"
   }
   b.ReadPointer "mapEventData", ->(b){
      persons = b.ReadByte "personCount"
      warps = b.ReadByte "warpCount"
      scripts = b.ReadByte "scriptCount"
      signposts = b.ReadByte "signpostCount"
      b.ReadArray "persons", persons, ->(b){
         b.ReadByte "id"; b.ReadByte "picture"; b.ReadShort "?"
         b.ReadShort "x"; b.ReadShort "y"
         b.ReadByte "?"; b.ReadByte "movementType"; b.ReadByte "movement"; b.ReadByte "?"
         b.ReadByte "isTrainer"; b.ReadByte "?"; b.ReadShort "viewRadius"
         b.ReadNullablePointer "script"
         b.ReadShort "id"; b.ReadShort "?"
      }
      b.ReadArray "warps", warps, ->(b){
         b.ReadShort "x"; b.ReadShort "y"
         b.ReadByte "?"; b.ReadByte "warp"; b.ReadByte "map"; b.ReadByte "bank" # TODO link
      }
      b.ReadArray "scripts", scripts, ->(b){
         b.ReadShort "x"; b.ReadShort "y"
         b.ReadShort "?"; b.ReadShort "scriptVariable"
         b.ReadShort "scriptVariableValue"; b.ReadShort "?"
         b.ReadNullablePointer "script"
      }
      b.ReadArray "signposts", signposts, ->(b){
         b.ReadShort "x"; b.ReadShort "y"
         b.ReadByte "talkingLevel"; b.ReadByte "signpostType"; b.ReadShort "?"
         b.ReadWord "?" # TODO script || -itemID .hiddenID .amount
      }
   }
   b.ReadNullablePointer "script"
   b.ReadNullablePointer "connections", ->(b){
      b.ReadWord "count"
      b.ReadNullablePointer "data"
   }
   b.ReadShort "song"; b.ReadShort "map"
   b.ReadByte "labelid"; b.ReadByte "flash"; b.ReadByte "weather"; b.ReadByte "type"
   b.ReadShort "_"; b.ReadByte "labelToggle"; b.ReadByte "_"
}

banks = types.FollowPointersUp maps
_map = types.FollowPointersUp banks
mapdata = _map[0].data
mapbank = _map[0].destination

