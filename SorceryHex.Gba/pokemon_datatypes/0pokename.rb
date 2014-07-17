types.WaitFor "_setup"

_pokename = 0x144
_pokename = 0xFA58 if types.Version == "AXVE" || types.Version == "AXPE"

self.pokename = types.ReadArray pokecount, _pokename, ->(b) {
   b.String 11, "name"
}

types.AddShortcut "pokename", pokename[0].Location