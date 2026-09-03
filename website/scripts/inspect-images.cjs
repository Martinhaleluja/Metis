const fs = require('fs');
const path = require('path');

function getJpgSize(filePath) {
  const buffer = fs.readFileSync(filePath);
  let i = 2; // skip SOI marker (FFD8)
  while (i < buffer.length) {
    if (buffer[i] === 0xFF) {
      const marker = buffer[i + 1];
      if (marker === 0xC0 || marker === 0xC2) { // SOF0 or SOF2
        const height = buffer.readUInt16BE(i + 5);
        const width = buffer.readUInt16BE(i + 7);
        return { width, height };
      }
      // skip marker content
      i += 2 + buffer.readUInt16BE(i + 2);
    } else {
      i++;
    }
  }
  return null;
}

const dir = 'c:\\Users\\halel\\Documents\\Lulu\\website\\public';
for (let j = 1; j <= 8; j++) {
  const name = `image${j}.jpg`;
  const p = path.join(dir, name);
  try {
    const size = getJpgSize(p);
    console.log(`${name}: ${size ? `${size.width}x${size.height}` : 'unknown'}`);
  } catch (err) {
    console.log(`${name}: error ${err.message}`);
  }
}
