// Compiles the styles of the components (Features/**, Pages/**): each _Name.scss becomes the _Name.css next to it, which the library serves.
// Sass's own directory mode is not used, as it takes every file whose name starts with an underscore for a partial, and the components'
// files are named that way. What they share (variables, mixins) is in Styles/, which is only imported and not compiled itself.
import { readdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import * as sass from 'sass';

const sourceFolders = ['Features', 'Pages'];

const files = sourceFolders.flatMap(folder => readdirSync(folder, { recursive: true, withFileTypes: true })
    .filter(entry => entry.isFile() && entry.name.endsWith('.scss'))
    .map(entry => join(entry.parentPath, entry.name)));

for (const file of files) {
    const result = sass.compile(file, { loadPaths: ['Styles'], style: 'expanded', sourceMap: false });
    writeFileSync(file.replace(/\.scss$/, '.css'), result.css + '\n');
}

console.log(`Compiled ${files.length} stylesheets.`);
