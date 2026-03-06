'use strict';

/**
 * Print Session Logger for BioBots
 *
 * Centralized event logging for bioprinting sessions. Records timestamped
 * events during a print run — nozzle switches, temperature changes, layer
 * completions, errors, pauses, user annotations — with structured metadata
 * for post-print analysis and regulatory traceability.
 *
 * Event categories:
 *   1. SYSTEM   — printer start/stop, firmware, connectivity
 *   2. NOZZLE   — nozzle switches, priming, clogging, cleaning
 *   3. THERMAL  — temperature set/reached/drift, UV lamp on/off
 *   4. MOTION   — layer start/complete, retraction, homing, travel
 *   5. MATERIAL — bioink load/change, pressure adjust, flow rate
 *   6. QUALITY  — inspection pass/fail, deviation detected
 *   7. SAFETY   — containment breach, emergency stop, alarm
 *   8. USER     — annotation, pause/resume, parameter override
 *
 * Severity levels: DEBUG, INFO, WARNING, ERROR, CRITICAL
 *
 * Usage:
 *   const logger = createSessionLogger();
 *   const session = logger.startSession({ printJobId: 'PJ-001' });
 *   session.log('NOZZLE', 'INFO', 'Nozzle 1 primed', { nozzleId: 1, pressure: 45 });
 *   session.logLayerComplete(5, { height: 0.25, duration: 12400 });
 *   session.logTemperature('bed', 37.2);
 *   const timeline = session.getTimeline();
 *   const exported = session.export('json');
 *   session.end('completed');
 */

function createSessionLogger(options) {
    options = options || {};

    var maxEventsPerSession = options.maxEvents || 50000;
    var maxSessions = options.maxSessions || 100;

    // ── Constants ────────────────────────────────────────────────

    var CATEGORIES = Object.freeze({
        SYSTEM:   'SYSTEM',
        NOZZLE:   'NOZZLE',
        THERMAL:  'THERMAL',
        MOTION:   'MOTION',
        MATERIAL: 'MATERIAL',
        QUALITY:  'QUALITY',
        SAFETY:   'SAFETY',
        USER:     'USER',
    });

    var SEVERITIES = Object.freeze({
        DEBUG:    'DEBUG',
        INFO:     'INFO',
        WARNING:  'WARNING',
        ERROR:    'ERROR',
        CRITICAL: 'CRITICAL',
    });

    var SEVERITY_RANK = { DEBUG: 0, INFO: 1, WARNING: 2, ERROR: 3, CRITICAL: 4 };

    var SESSION_STATES = Object.freeze({
        ACTIVE:    'active',
        PAUSED:    'paused',
        COMPLETED: 'completed',
        ABORTED:   'aborted',
        ERROR:     'error',
    });

    // ── Session Storage ─────────────────────────────────────────

    var sessions = {};
    var sessionOrder = [];
    var nextEventId = 1;

    // ── Helpers ─────────────────────────────────────────────────

    function generateSessionId() {
        var ts = Date.now().toString(36);
        var rand = Math.random().toString(36).substring(2, 8);
        return 'SES-' + ts + '-' + rand;
    }

    function now() {
        return Date.now();
    }

    function validateCategory(cat) {
        if (!CATEGORIES[cat]) {
            throw new Error('Invalid category: ' + cat + '. Valid: ' + Object.keys(CATEGORIES).join(', '));
        }
    }

    function validateSeverity(sev) {
        if (SEVERITY_RANK[sev] === undefined) {
            throw new Error('Invalid severity: ' + sev + '. Valid: ' + Object.keys(SEVERITIES).join(', '));
        }
    }

    function evictOldSessions() {
        while (sessionOrder.length > maxSessions) {
            var oldId = sessionOrder.shift();
            delete sessions[oldId];
        }
    }

    // ── Session Factory ─────────────────────────────────────────

    function startSession(meta) {
        meta = meta || {};

        var sessionId = generateSessionId();
        var startTime = now();

        var events = [];
        var eventIndex = {};  // category -> [indices]
        var state = SESSION_STATES.ACTIVE;
        var pauseTime = null;
        var totalPauseMs = 0;
        var layerCount = 0;
        var errorCount = 0;
        var warningCount = 0;

        var sessionMeta = {
            printJobId: meta.printJobId || null,
            printerName: meta.printerName || null,
            operator: meta.operator || null,
            bioinks: meta.bioinks || [],
            protocol: meta.protocol || null,
            notes: meta.notes || null,
        };

        // Initialize category index
        Object.keys(CATEGORIES).forEach(function (cat) {
            eventIndex[cat] = [];
        });

        // ── Core Logging ────────────────────────────────────────

        function log(category, severity, message, data) {
            if (state !== SESSION_STATES.ACTIVE && state !== SESSION_STATES.PAUSED) {
                return { error: 'Session is ' + state + ', cannot log events' };
            }

            validateCategory(category);
            validateSeverity(severity);

            if (events.length >= maxEventsPerSession) {
                return { error: 'Max events (' + maxEventsPerSession + ') reached' };
            }

            var eventId = nextEventId++;
            var timestamp = now();
            var elapsed = timestamp - startTime - totalPauseMs;

            var event = {
                id: eventId,
                timestamp: timestamp,
                elapsed: elapsed,
                category: category,
                severity: severity,
                message: message || '',
                data: data || null,
                layer: layerCount,
            };

            events.push(event);
            eventIndex[category].push(events.length - 1);

            if (severity === 'ERROR' || severity === 'CRITICAL') errorCount++;
            if (severity === 'WARNING') warningCount++;

            return { eventId: eventId, timestamp: timestamp, elapsed: elapsed };
        }

        // ── Convenience Loggers ─────────────────────────────────

        function logLayerComplete(layerNum, data) {
            layerCount = layerNum;
            return log('MOTION', 'INFO', 'Layer ' + layerNum + ' complete',
                Object.assign({ layerNumber: layerNum }, data || {}));
        }

        function logTemperature(zone, tempC, data) {
            var severity = 'INFO';
            if (tempC > 42 || tempC < 15) severity = 'WARNING';
            if (tempC > 50 || tempC < 4) severity = 'ERROR';
            return log('THERMAL', severity, zone + ' temperature: ' + tempC.toFixed(1) + '\u00B0C',
                Object.assign({ zone: zone, temperature: tempC }, data || {}));
        }

        function logNozzleSwitch(fromNozzle, toNozzle, data) {
            return log('NOZZLE', 'INFO', 'Switched nozzle ' + fromNozzle + ' \u2192 ' + toNozzle,
                Object.assign({ from: fromNozzle, to: toNozzle }, data || {}));
        }

        function logPressure(nozzleId, pressureKPa, data) {
            var severity = 'INFO';
            if (pressureKPa > 200) severity = 'WARNING';
            if (pressureKPa > 350) severity = 'ERROR';
            return log('MATERIAL', severity, 'Nozzle ' + nozzleId + ' pressure: ' + pressureKPa + ' kPa',
                Object.assign({ nozzleId: nozzleId, pressure: pressureKPa }, data || {}));
        }

        function logError(message, data) {
            return log('SYSTEM', 'ERROR', message, data);
        }

        function logAnnotation(message, data) {
            return log('USER', 'INFO', message, Object.assign({ type: 'annotation' }, data || {}));
        }

        function logSafetyAlert(message, data) {
            return log('SAFETY', 'CRITICAL', message, data);
        }

        // ── Session Control ─────────────────────────────────────

        function pause(reason) {
            if (state !== SESSION_STATES.ACTIVE) {
                return { error: 'Cannot pause: session is ' + state };
            }
            state = SESSION_STATES.PAUSED;
            pauseTime = now();
            log('USER', 'INFO', 'Session paused' + (reason ? ': ' + reason : ''), { reason: reason });
            return { state: state, pausedAt: pauseTime };
        }

        function resume() {
            if (state !== SESSION_STATES.PAUSED) {
                return { error: 'Cannot resume: session is ' + state };
            }
            var pauseDuration = now() - pauseTime;
            totalPauseMs += pauseDuration;
            state = SESSION_STATES.ACTIVE;
            log('USER', 'INFO', 'Session resumed after ' + (pauseDuration / 1000).toFixed(1) + 's',
                { pauseDuration: pauseDuration });
            pauseTime = null;
            return { state: state, pauseDuration: pauseDuration };
        }

        function end(outcome) {
            if (state === SESSION_STATES.COMPLETED || state === SESSION_STATES.ABORTED) {
                return { error: 'Session already ended' };
            }
            if (state === SESSION_STATES.PAUSED) {
                totalPauseMs += now() - pauseTime;
                pauseTime = null;
            }

            var endTime = now();
            var activeTime = endTime - startTime - totalPauseMs;

            outcome = outcome || 'completed';
            if (outcome === 'completed') state = SESSION_STATES.COMPLETED;
            else if (outcome === 'aborted') state = SESSION_STATES.ABORTED;
            else if (outcome === 'error') state = SESSION_STATES.ERROR;
            else state = SESSION_STATES.COMPLETED;

            log('SYSTEM', outcome === 'completed' ? 'INFO' : 'WARNING',
                'Session ended: ' + outcome, { outcome: outcome, activeTime: activeTime });

            return {
                sessionId: sessionId,
                state: state,
                startTime: startTime,
                endTime: endTime,
                activeTimeMs: activeTime,
                totalPauseMs: totalPauseMs,
                totalEvents: events.length,
                layers: layerCount,
                errors: errorCount,
                warnings: warningCount,
            };
        }

        // ── Query & Filter ──────────────────────────────────────

        function getEvents(filter) {
            filter = filter || {};

            var result = events;

            if (filter.category) {
                var catIndices = eventIndex[filter.category];
                if (!catIndices) return [];
                result = catIndices.map(function (i) { return events[i]; });
            }

            if (filter.severity) {
                var minRank = SEVERITY_RANK[filter.severity] || 0;
                result = result.filter(function (e) {
                    return SEVERITY_RANK[e.severity] >= minRank;
                });
            }

            if (filter.since) {
                result = result.filter(function (e) { return e.timestamp >= filter.since; });
            }

            if (filter.until) {
                result = result.filter(function (e) { return e.timestamp <= filter.until; });
            }

            if (filter.layer !== undefined) {
                result = result.filter(function (e) { return e.layer === filter.layer; });
            }

            if (filter.search) {
                var term = filter.search.toLowerCase();
                result = result.filter(function (e) {
                    return e.message.toLowerCase().indexOf(term) !== -1;
                });
            }

            if (filter.limit && filter.limit > 0) {
                result = result.slice(-filter.limit);
            }

            return result;
        }

        function getTimeline(bucketMs) {
            bucketMs = bucketMs || 60000;  // 1-minute buckets by default

            if (events.length === 0) return { buckets: [], summary: {} };

            var firstTs = events[0].timestamp;
            var lastTs = events[events.length - 1].timestamp;
            var buckets = [];

            for (var t = firstTs; t <= lastTs; t += bucketMs) {
                var bucketEnd = t + bucketMs;
                var bucketEvents = events.filter(function (e) {
                    return e.timestamp >= t && e.timestamp < bucketEnd;
                });

                var categoryCounts = {};
                var severityCounts = {};
                bucketEvents.forEach(function (e) {
                    categoryCounts[e.category] = (categoryCounts[e.category] || 0) + 1;
                    severityCounts[e.severity] = (severityCounts[e.severity] || 0) + 1;
                });

                buckets.push({
                    start: t,
                    end: bucketEnd,
                    count: bucketEvents.length,
                    categories: categoryCounts,
                    severities: severityCounts,
                    hasErrors: bucketEvents.some(function (e) {
                        return e.severity === 'ERROR' || e.severity === 'CRITICAL';
                    }),
                });
            }

            // Summary across categories
            var summary = {};
            Object.keys(CATEGORIES).forEach(function (cat) {
                summary[cat] = eventIndex[cat].length;
            });

            return { buckets: buckets, summary: summary, bucketMs: bucketMs };
        }

        function getSeverityBreakdown() {
            var breakdown = { DEBUG: 0, INFO: 0, WARNING: 0, ERROR: 0, CRITICAL: 0 };
            events.forEach(function (e) {
                breakdown[e.severity]++;
            });
            return breakdown;
        }

        function getCategoryBreakdown() {
            var breakdown = {};
            Object.keys(CATEGORIES).forEach(function (cat) {
                breakdown[cat] = eventIndex[cat].length;
            });
            return breakdown;
        }

        function getLastEvents(n) {
            n = n || 10;
            return events.slice(-n);
        }

        function getErrorsAndWarnings() {
            return events.filter(function (e) {
                return SEVERITY_RANK[e.severity] >= SEVERITY_RANK.WARNING;
            });
        }

        // ── Export ──────────────────────────────────────────────

        function exportSession(format) {
            format = format || 'json';

            var sessionData = {
                sessionId: sessionId,
                state: state,
                meta: sessionMeta,
                startTime: startTime,
                endTime: state !== SESSION_STATES.ACTIVE ? events[events.length - 1].timestamp : null,
                activeTimeMs: now() - startTime - totalPauseMs,
                totalPauseMs: totalPauseMs,
                layers: layerCount,
                totalEvents: events.length,
                errors: errorCount,
                warnings: warningCount,
                severityBreakdown: getSeverityBreakdown(),
                categoryBreakdown: getCategoryBreakdown(),
                events: events,
            };

            if (format === 'json') {
                return JSON.stringify(sessionData, null, 2);
            }

            if (format === 'csv') {
                var lines = ['id,timestamp,elapsed_ms,category,severity,layer,message'];
                events.forEach(function (e) {
                    var msg = '"' + (e.message || '').replace(/"/g, '""') + '"';
                    lines.push([e.id, e.timestamp, e.elapsed, e.category, e.severity, e.layer, msg].join(','));
                });
                return lines.join('\n');
            }

            if (format === 'summary') {
                var dur = (sessionData.activeTimeMs / 1000).toFixed(1);
                var lines2 = [
                    '===================================================',
                    '  BIOPRINT SESSION REPORT -- ' + sessionId,
                    '===================================================',
                    '',
                    '  Print Job:    ' + (sessionMeta.printJobId || 'N/A'),
                    '  Printer:      ' + (sessionMeta.printerName || 'N/A'),
                    '  Operator:     ' + (sessionMeta.operator || 'N/A'),
                    '  Protocol:     ' + (sessionMeta.protocol || 'N/A'),
                    '  Status:       ' + state.toUpperCase(),
                    '',
                    '  Duration:     ' + dur + 's active, ' + (totalPauseMs / 1000).toFixed(1) + 's paused',
                    '  Layers:       ' + layerCount,
                    '  Events:       ' + events.length,
                    '  Errors:       ' + errorCount,
                    '  Warnings:     ' + warningCount,
                    '',
                    '  -- Category Breakdown --',
                ];
                var catBreak = getCategoryBreakdown();
                Object.keys(catBreak).forEach(function (cat) {
                    if (catBreak[cat] > 0) {
                        lines2.push('    ' + cat.padEnd(12) + catBreak[cat]);
                    }
                });

                var errs = getErrorsAndWarnings();
                if (errs.length > 0) {
                    lines2.push('');
                    lines2.push('  -- Issues --');
                    errs.slice(0, 20).forEach(function (e) {
                        var time = ((e.elapsed) / 1000).toFixed(1) + 's';
                        lines2.push('    [' + e.severity + '] ' + time + ' -- ' + e.message);
                    });
                    if (errs.length > 20) {
                        lines2.push('    ... and ' + (errs.length - 20) + ' more');
                    }
                }

                lines2.push('');
                lines2.push('===================================================');
                return lines2.join('\n');
            }

            return JSON.stringify(sessionData, null, 2);
        }

        // ── Session Status ──────────────────────────────────────

        function getStatus() {
            var elapsed = now() - startTime - totalPauseMs;
            return {
                sessionId: sessionId,
                state: state,
                meta: sessionMeta,
                elapsedMs: elapsed,
                totalPauseMs: totalPauseMs,
                layers: layerCount,
                totalEvents: events.length,
                errors: errorCount,
                warnings: warningCount,
                eventsPerMinute: elapsed > 0 ? (events.length / (elapsed / 60000)).toFixed(2) : '0',
            };
        }

        // ── Public Session Interface ────────────────────────────

        var session = {
            id: sessionId,
            log: log,
            logLayerComplete: logLayerComplete,
            logTemperature: logTemperature,
            logNozzleSwitch: logNozzleSwitch,
            logPressure: logPressure,
            logError: logError,
            logAnnotation: logAnnotation,
            logSafetyAlert: logSafetyAlert,
            pause: pause,
            resume: resume,
            end: end,
            getEvents: getEvents,
            getTimeline: getTimeline,
            getSeverityBreakdown: getSeverityBreakdown,
            getCategoryBreakdown: getCategoryBreakdown,
            getLastEvents: getLastEvents,
            getErrorsAndWarnings: getErrorsAndWarnings,
            getStatus: getStatus,
            export: exportSession,
        };

        sessions[sessionId] = session;
        sessionOrder.push(sessionId);
        evictOldSessions();

        log('SYSTEM', 'INFO', 'Session started', { meta: sessionMeta });

        return session;
    }

    // ── Logger-Level API ────────────────────────────────────────

    function getSession(sessionId) {
        return sessions[sessionId] || null;
    }

    function listSessions() {
        return sessionOrder.map(function (id) {
            var s = sessions[id];
            return s ? s.getStatus() : null;
        }).filter(Boolean);
    }

    function getActiveSessions() {
        return listSessions().filter(function (s) {
            return s.state === SESSION_STATES.ACTIVE || s.state === SESSION_STATES.PAUSED;
        });
    }

    function reset() {
        sessions = {};
        sessionOrder = [];
        nextEventId = 1;
    }

    return {
        startSession: startSession,
        getSession: getSession,
        listSessions: listSessions,
        getActiveSessions: getActiveSessions,
        reset: reset,
        CATEGORIES: CATEGORIES,
        SEVERITIES: SEVERITIES,
    };
}

if (typeof module !== 'undefined' && module.exports) {
    module.exports = { createSessionLogger: createSessionLogger };
}
