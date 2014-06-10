SorceryHex
==========

Custom Hex Editor

SorceryHex is a concept for a smarter, more capable hex editor.
Unlike alternative hex editors, it provide smart interpretations and editing tools to match your data's format.
Unlike alternative data-specific editors, it gives you a full view of your data and lets you control every aspect of your data.

It is in the concept phase, and only contains smart interpretations for .gba files (ROM files for the Nintendo Game Boy Advance).

## Current controls: ##
* Ctrl+I: Open the interpreter, a panel that guesses at the meaning of the data onscreen.
* Ctrl+Click or Double-Click: Follow a pointer, or return to a pointer source.
* Ctrl+- or Backspace: Return to previous location. You can also do this using a "back" button on a mouse or keyboard.
* Ctrl+Left/Right: Shift the data alignment left/right.
* Ctrl+F: Open the Find menu. You can search for hex or text.
* Ctrl+G: Go to position.
* Ctrl+Up/Down: Shift the data up/down.
* Click-Drag: Highlight a set of data.
* Hover over an item in the interpretation pane: highlight the data being interpreted.
* Resize: Change the bytes per line.

## Ruby ##
Ctrl+R opens the ruby interpreter. A few special variables / functions have been included for your convinience.
The ruby interpreter is useful when you're trying to learn about the data. The ruby interpreter can help you understand the structure of the data: instead of searching for something manually, try using the ruby interpreter to speed up the process.

* **Hex Interpreter**: Any value returned from the interpreter will be output as hex.
* **app.find("term")**: This does the same thing as the Find command, except the results are returned to the script. This lets you do some interesting post-processing on the search results, such as filtering, sorting, or counting.
* **app.goto**: The goto command does the same thing as Ctrl+G.
* **app.data**: Represents the data currently loaded by the editor. You can view it, parse it, or edit it at your will.

