import os
import codecs

file_path = r"c:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\StreamlitAudit\app.py"

try:
    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()

    # Step 1: Unescape the unicode sequences I created
    # This turns "\\u00f0" back into the character "ð"
    try:
        # codecs.decode with 'unicode_escape' works on bytes usually, or strings with escapes
        # convert to bytes first to be safe
        unescaped = codecs.decode(content, 'unicode_escape')
    except Exception as e:
        print(f"Error during unescape: {e}")
        # manual fallback?
        unescaped = content

    # Step 2: "Magic Decode"
    # The string 'unescaped' now contains Mojibake (e.g. ðŸ...)
    # We want to reinterpret these characters as bytes (Latin1) and then read as UTF-8
    
    new_content = ""
    
    # We process line by line to avoid crashing on lines that are actually fine
    for line in unescaped.splitlines(keepends=True):
        try:
            # Check if line needs fixing (contains chars > 127)
            if any(ord(c) > 127 for c in line):
                # Encode to latin1 (maps chars 0-255 to bytes 0-255 directly)
                # If a char is > 255, latin1 will fail. 
                # This ensures we only fix the Mojibake which is usually in latin1 range.
                b = line.encode('latin1') 
                fixed = b.decode('utf-8')
                new_content += fixed
            else:
                new_content += line
        except (UnicodeEncodeError, UnicodeDecodeError):
            # If it fails, keep original 
            # (e.g. if we have real higher unicode that can't be latin1 encoded)
            new_content += line

    # Step 3: Write back
    if new_content != content:
        # We write as UTF-8. 
        # Streamlit should handle UTF-8 source files fine if there's no BOM.
        
        # Ensure header
        if not new_content.startswith("# -*- coding: utf-8 -*-"):
            new_content = "# -*- coding: utf-8 -*-\n" + new_content
            
        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(new_content)
        print("Applied Magic Decode successfully.")
    else:
        print("No changes made.")

except Exception as e:
    print(f"Critical Error: {e}")
