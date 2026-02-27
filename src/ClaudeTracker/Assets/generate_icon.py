# Generate a simple ICO file with a purple circle (ClaudeTracker icon)
import struct, io, zlib

def create_ico():
    # Create a 32x32 RGBA image: purple circle on transparent background
    size = 32
    pixels = bytearray()
    center = size / 2
    radius = 13
    
    for y in range(size):
        for x in range(size):
            dx = x - center + 0.5
            dy = y - center + 0.5
            dist = (dx*dx + dy*dy) ** 0.5
            if dist <= radius:
                # Deep purple (#673AB7)
                pixels.extend([0x67, 0x3A, 0xB7, 0xFF])
            elif dist <= radius + 1:
                # Anti-aliased edge
                alpha = int(255 * (1 - (dist - radius)))
                pixels.extend([0x67, 0x3A, 0xB7, alpha])
            else:
                pixels.extend([0, 0, 0, 0])
    
    # Create PNG data
    def create_png(width, height, rgba_data):
        def chunk(chunk_type, data):
            c = chunk_type + data
            crc = struct.pack('>I', zlib.crc32(c) & 0xffffffff)
            return struct.pack('>I', len(data)) + c + crc
        
        raw_data = bytearray()
        for y in range(height):
            raw_data.append(0)  # filter: none
            offset = y * width * 4
            raw_data.extend(rgba_data[offset:offset + width * 4])
        
        compressed = zlib.compress(bytes(raw_data))
        
        png = b'\x89PNG\r\n\x1a\n'
        png += chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
        png += chunk(b'IDAT', compressed)
        png += chunk(b'IEND', b'')
        return png
    
    png_data = create_png(size, size, bytes(pixels))
    
    # ICO format
    ico = bytearray()
    ico.extend(struct.pack('<HHH', 0, 1, 1))  # Reserved, Type=ICO, Count=1
    ico.extend(struct.pack('<BBBBHHII', 
        size, size, 0, 0,  # Width, Height, Colors, Reserved
        1, 32,  # Planes, BPP
        len(png_data),  # Size
        22  # Offset (6 header + 16 entry)
    ))
    ico.extend(png_data)
    return bytes(ico)

with open('src/ClaudeTracker/Assets/app_icon.ico', 'wb') as f:
    f.write(create_ico())
print("Icon created successfully")
