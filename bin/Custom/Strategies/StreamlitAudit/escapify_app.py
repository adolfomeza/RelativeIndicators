import os

file_path = r"c:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\StreamlitAudit\app.py"

try:
    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()

    # The goal is to produce a file that is valid ASCII.
    # We can rely on Python's 'unicode_escape' codec?
    # No, unicode_escape escapes backslashes too.
    
    # We want: 'á' -> '\u00e1'
    
    new_chars = []
    changes = 0
    
    for char in content:
        if ord(char) > 127:
            # Escape it
            # Format: \uXXXX or \UXXXXXXXX
            # For Basic Multilingual Plane (BMP)
            if ord(char) <= 0xFFFF:
                # Use standard lowercase hex format
                escaped = f"\\u{ord(char):04x}"
            else:
                escaped = f"\\U{ord(char):08x}"
            new_chars.append(escaped)
            changes += 1
        else:
            new_chars.append(char)
            
    final_text = "".join(new_chars)
    
    # Ensure header is present, though maybe not strictly needed if pure ASCII
    if not final_text.startswith("# -*-"):
         final_text = "# -*- coding: utf-8 -*-\n" + final_text

    if changes > 0:
        # Write as 'utf-8' but it will be pure ASCII chars
        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(final_text)
        print(f"Applied ASCII-fication. Escaped {changes} non-ASCII characters.")
    else:
        print("No non-ASCII characters found.")

except Exception as e:
    print(f"Error: {e}")
