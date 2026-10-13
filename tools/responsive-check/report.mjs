// Writes the sweep results as report.json (everything) and report.md (summary and defects).
import { writeFileSync } from 'node:fs';
import { join } from 'node:path';

function countDefectsByType(results) {
    const counts = {};
    for (const result of results) {
        for (const defect of result.defects) {
            counts[defect.type] = (counts[defect.type] ?? 0) + 1;
        }
    }

    return counts;
}

function listSection(title, lines) {
    if (lines.length === 0) {
        return [`## ${title}`, '', 'None.', ''];
    }

    return [`## ${title}`, '', ...lines.map(line => `- ${line}`), ''];
}

function defectLines(results) {
    const lines = [];
    for (const result of results) {
        for (const defect of result.defects) {
            lines.push(`\`${result.role} ${result.language} ${result.viewport}\` ${result.url} - **${defect.type}**: ${defect.detail}`);
        }
    }

    return lines;
}

function englishTextLines(results) {
    const lines = [];
    for (const result of results) {
        if (result.englishTexts.length > 0) {
            lines.push(`\`${result.role} ${result.viewport}\` ${result.url}: ${result.englishTexts.map(text => `"${text.slice(0, 80)}"`).join(', ')}`);
        }
    }

    return lines;
}

/** Returns the totals so the caller can print them and set the exit code. */
export function writeReports(outputFolder, sweep) {
    const { results } = sweep;
    const defectCounts = countDefectsByType(results);
    const defectTotal = Object.values(defectCounts).reduce((total, count) => total + count, 0);
    const uniquePages = new Set(results.map(result => `${result.role} ${result.url}`)).size;
    const cleanVisits = results.filter(result => result.defects.length === 0).length;
    const ignored = [...new Set(results.flatMap(result => result.ignored))];

    writeFileSync(join(outputFolder, 'report.json'), JSON.stringify({ ...sweep, defectCounts }, null, 2));

    const markdown = [
        '# Responsive check',
        '',
        `Base URL: ${sweep.baseUrl}. ${uniquePages} role/page pairs, ${results.length} visits (each pair at 2 sizes in 2 languages), ${cleanVisits} clean, ${defectTotal} defect(s).`,
        '',
        '| Defect type | Count |',
        '|---|---|',
        ...Object.entries(defectCounts).map(([type, count]) => `| ${type} | ${count} |`),
        '',
        ...listSection('Routes not covered by the page lists', sweep.uncoveredRoutes),
        ...listSection('Skipped roles', sweep.skippedRoles),
        ...listSection('Skipped pages', sweep.skippedPages),
        ...listSection('Defects', defectLines(results)),
        ...listSection('English-looking text on Greek pages (review by hand)', englishTextLines(results)),
        ...listSection('Ignored browser messages', ignored.slice(0, 30)),
    ].join('\n');
    writeFileSync(join(outputFolder, 'report.md'), markdown);

    return { visits: results.length, defects: defectTotal };
}
