'use strict';

const assert = require('assert');
const { createSessionLogger } = require('../Try/scripts/sessionLogger');

describe('Print Session Logger', () => {

    let logger;

    beforeEach(() => {
        logger = createSessionLogger();
    });

    describe('startSession', () => {
        it('creates a session with an ID', () => {
            const s = logger.startSession();
            assert.ok(s.id.startsWith('SES-'));
        });

        it('accepts metadata', () => {
            const s = logger.startSession({ printJobId: 'PJ-001', operator: 'Dr. Smith' });
            const status = s.getStatus();
            assert.strictEqual(status.meta.printJobId, 'PJ-001');
            assert.strictEqual(status.meta.operator, 'Dr. Smith');
        });

        it('starts in active state', () => {
            const s = logger.startSession();
            assert.strictEqual(s.getStatus().state, 'active');
        });

        it('auto-logs session started event', () => {
            const s = logger.startSession();
            const events = s.getEvents({ category: 'SYSTEM' });
            assert.ok(events.length >= 1);
            assert.ok(events[0].message.indexOf('Session started') !== -1);
        });
    });

    describe('log', () => {
        it('records an event with correct fields', () => {
            const s = logger.startSession();
            const result = s.log('NOZZLE', 'INFO', 'Primed nozzle 1', { nozzleId: 1 });
            assert.ok(result.eventId > 0);
            assert.ok(result.timestamp > 0);
            assert.ok(result.elapsed >= 0);
        });

        it('rejects invalid category', () => {
            const s = logger.startSession();
            assert.throws(() => { s.log('INVALID', 'INFO', 'test'); });
        });

        it('rejects invalid severity', () => {
            const s = logger.startSession();
            assert.throws(() => { s.log('SYSTEM', 'MEGA', 'test'); });
        });

        it('tracks error count', () => {
            const s = logger.startSession();
            s.log('SYSTEM', 'ERROR', 'err1');
            s.log('SYSTEM', 'CRITICAL', 'err2');
            s.log('SYSTEM', 'INFO', 'ok');
            assert.strictEqual(s.getStatus().errors, 2);
        });

        it('tracks warning count', () => {
            const s = logger.startSession();
            s.log('THERMAL', 'WARNING', 'temp drift');
            s.log('THERMAL', 'WARNING', 'temp drift 2');
            assert.strictEqual(s.getStatus().warnings, 2);
        });

        it('prevents logging after session ends', () => {
            const s = logger.startSession();
            s.end('completed');
            const result = s.log('SYSTEM', 'INFO', 'too late');
            assert.ok(result.error);
        });
    });

    describe('convenience loggers', () => {
        it('logLayerComplete increments layer count', () => {
            const s = logger.startSession();
            s.logLayerComplete(1, { height: 0.25 });
            s.logLayerComplete(2, { height: 0.25 });
            assert.strictEqual(s.getStatus().layers, 2);
        });

        it('logTemperature auto-warns on extremes', () => {
            const s = logger.startSession();
            s.logTemperature('bed', 37.0);
            s.logTemperature('nozzle', 50.5);
            const warnings = s.getErrorsAndWarnings();
            assert.ok(warnings.length >= 1);
        });

        it('logNozzleSwitch records from/to', () => {
            const s = logger.startSession();
            s.logNozzleSwitch(1, 2, { reason: 'material change' });
            const events = s.getEvents({ category: 'NOZZLE' });
            assert.ok(events.some(e => e.data && e.data.from === 1 && e.data.to === 2));
        });

        it('logPressure warns on high pressure', () => {
            const s = logger.startSession();
            s.logPressure(1, 250);
            assert.strictEqual(s.getStatus().warnings, 1);
        });

        it('logPressure errors on very high pressure', () => {
            const s = logger.startSession();
            s.logPressure(1, 400);
            assert.strictEqual(s.getStatus().errors, 1);
        });

        it('logError records system error', () => {
            const s = logger.startSession();
            s.logError('Motor stall detected', { motorId: 2 });
            assert.strictEqual(s.getStatus().errors, 1);
        });

        it('logAnnotation records user note', () => {
            const s = logger.startSession();
            s.logAnnotation('Looks good at layer 5');
            const events = s.getEvents({ category: 'USER' });
            assert.ok(events.some(e => e.message === 'Looks good at layer 5'));
        });

        it('logSafetyAlert records critical safety event', () => {
            const s = logger.startSession();
            s.logSafetyAlert('UV lamp malfunction');
            const critical = s.getEvents({ severity: 'CRITICAL' });
            assert.ok(critical.length >= 1);
        });
    });

    describe('pause/resume', () => {
        it('pauses and resumes session', () => {
            const s = logger.startSession();
            const p = s.pause('bioink refill');
            assert.strictEqual(p.state, 'paused');
            const r = s.resume();
            assert.strictEqual(r.state, 'active');
            assert.ok(r.pauseDuration >= 0);
        });

        it('cannot pause when not active', () => {
            const s = logger.startSession();
            s.end('completed');
            const result = s.pause();
            assert.ok(result.error);
        });

        it('cannot resume when not paused', () => {
            const s = logger.startSession();
            const result = s.resume();
            assert.ok(result.error);
        });

        it('tracks total pause time', () => {
            const s = logger.startSession();
            s.pause();
            s.resume();
            assert.ok(s.getStatus().totalPauseMs >= 0);
        });
    });

    describe('end', () => {
        it('returns session summary', () => {
            const s = logger.startSession();
            s.logLayerComplete(1);
            s.logLayerComplete(2);
            s.log('THERMAL', 'WARNING', 'drift');
            const summary = s.end('completed');
            assert.strictEqual(summary.state, 'completed');
            assert.strictEqual(summary.layers, 2);
            assert.strictEqual(summary.warnings, 1);
            assert.ok(summary.totalEvents > 0);
        });

        it('cannot end twice', () => {
            const s = logger.startSession();
            s.end('completed');
            const result = s.end('completed');
            assert.ok(result.error);
        });

        it('handles aborted outcome', () => {
            const s = logger.startSession();
            const summary = s.end('aborted');
            assert.strictEqual(summary.state, 'aborted');
        });
    });

    describe('getEvents', () => {
        it('filters by category', () => {
            const s = logger.startSession();
            s.log('NOZZLE', 'INFO', 'n1');
            s.log('THERMAL', 'INFO', 't1');
            s.log('NOZZLE', 'INFO', 'n2');
            const nozzle = s.getEvents({ category: 'NOZZLE' });
            assert.strictEqual(nozzle.length, 2);
        });

        it('filters by severity', () => {
            const s = logger.startSession();
            s.log('SYSTEM', 'DEBUG', 'd');
            s.log('SYSTEM', 'INFO', 'i');
            s.log('SYSTEM', 'WARNING', 'w');
            s.log('SYSTEM', 'ERROR', 'e');
            const warnings = s.getEvents({ severity: 'WARNING' });
            assert.strictEqual(warnings.length, 2);  // WARNING + ERROR
        });

        it('filters by search term', () => {
            const s = logger.startSession();
            s.log('SYSTEM', 'INFO', 'motor A started');
            s.log('SYSTEM', 'INFO', 'heater B activated');
            s.log('SYSTEM', 'INFO', 'motor B started');
            const motors = s.getEvents({ search: 'motor' });
            assert.strictEqual(motors.length, 2);
        });

        it('respects limit', () => {
            const s = logger.startSession();
            for (let i = 0; i < 20; i++) {
                s.log('SYSTEM', 'INFO', 'event ' + i);
            }
            const limited = s.getEvents({ limit: 5 });
            assert.strictEqual(limited.length, 5);
        });

        it('filters by layer', () => {
            const s = logger.startSession();
            s.logLayerComplete(1);
            s.log('QUALITY', 'INFO', 'check at layer 1');
            s.logLayerComplete(2);
            s.log('QUALITY', 'INFO', 'check at layer 2');
            const layer1 = s.getEvents({ layer: 1 });
            assert.ok(layer1.length >= 1);
        });
    });

    describe('getTimeline', () => {
        it('returns bucketed timeline', () => {
            const s = logger.startSession();
            s.log('SYSTEM', 'INFO', 'e1');
            s.log('NOZZLE', 'INFO', 'e2');
            const tl = s.getTimeline(60000);
            assert.ok(tl.buckets.length >= 1);
            assert.ok(tl.summary);
            assert.strictEqual(tl.bucketMs, 60000);
        });

        it('returns at least one bucket for any events', () => {
            const s = logger.startSession();
            const tl = s.getTimeline();
            assert.ok(tl.buckets.length >= 1);
        });
    });

    describe('severity/category breakdown', () => {
        it('counts by severity', () => {
            const s = logger.startSession();
            s.log('SYSTEM', 'INFO', 'i');
            s.log('SYSTEM', 'WARNING', 'w');
            s.log('SYSTEM', 'ERROR', 'e');
            const bd = s.getSeverityBreakdown();
            assert.ok(bd.INFO >= 1);
            assert.strictEqual(bd.WARNING, 1);
            assert.strictEqual(bd.ERROR, 1);
        });

        it('counts by category', () => {
            const s = logger.startSession();
            s.log('NOZZLE', 'INFO', 'n');
            s.log('THERMAL', 'INFO', 't');
            s.log('NOZZLE', 'INFO', 'n2');
            const bd = s.getCategoryBreakdown();
            assert.strictEqual(bd.NOZZLE, 2);
            assert.strictEqual(bd.THERMAL, 1);
        });
    });

    describe('getLastEvents / getErrorsAndWarnings', () => {
        it('returns last N events', () => {
            const s = logger.startSession();
            for (let i = 0; i < 15; i++) {
                s.log('SYSTEM', 'INFO', 'e' + i);
            }
            const last5 = s.getLastEvents(5);
            assert.strictEqual(last5.length, 5);
        });

        it('returns only warnings+', () => {
            const s = logger.startSession();
            s.log('SYSTEM', 'INFO', 'ok');
            s.log('SYSTEM', 'WARNING', 'warn');
            s.log('SYSTEM', 'ERROR', 'err');
            s.log('SYSTEM', 'DEBUG', 'dbg');
            const issues = s.getErrorsAndWarnings();
            assert.strictEqual(issues.length, 2);
        });
    });

    describe('export', () => {
        it('exports JSON', () => {
            const s = logger.startSession({ printJobId: 'PJ-TEST' });
            s.log('NOZZLE', 'INFO', 'primed');
            const json = s.export('json');
            const parsed = JSON.parse(json);
            assert.strictEqual(parsed.sessionId, s.id);
            assert.strictEqual(parsed.meta.printJobId, 'PJ-TEST');
            assert.ok(parsed.events.length >= 2);
        });

        it('exports CSV', () => {
            const s = logger.startSession();
            s.log('SYSTEM', 'INFO', 'test event');
            const csv = s.export('csv');
            assert.ok(csv.startsWith('id,timestamp'));
            const lines = csv.split('\n');
            assert.ok(lines.length >= 3); // header + 2 events
        });

        it('exports summary report', () => {
            const s = logger.startSession({ printJobId: 'PJ-002', operator: 'Dr. Jones' });
            s.logLayerComplete(1);
            s.log('THERMAL', 'WARNING', 'temp drift');
            s.end('completed');
            const report = s.export('summary');
            assert.ok(report.indexOf('PJ-002') !== -1);
            assert.ok(report.indexOf('Dr. Jones') !== -1);
            assert.ok(report.indexOf('COMPLETED') !== -1);
        });
    });

    describe('logger-level API', () => {
        it('lists sessions', () => {
            logger.startSession({ printJobId: 'A' });
            logger.startSession({ printJobId: 'B' });
            const list = logger.listSessions();
            assert.strictEqual(list.length, 2);
        });

        it('gets session by ID', () => {
            const s = logger.startSession();
            const found = logger.getSession(s.id);
            assert.ok(found);
            assert.strictEqual(found.id, s.id);
        });

        it('getActiveSessions excludes ended', () => {
            const s1 = logger.startSession();
            logger.startSession();
            s1.end('completed');
            const active = logger.getActiveSessions();
            assert.strictEqual(active.length, 1);
        });

        it('reset clears all sessions', () => {
            logger.startSession();
            logger.startSession();
            logger.reset();
            assert.strictEqual(logger.listSessions().length, 0);
        });
    });

    describe('max events limit', () => {
        it('stops logging when maxEvents reached', () => {
            const small = createSessionLogger({ maxEvents: 5 });
            const s = small.startSession();
            for (let i = 0; i < 10; i++) {
                s.log('SYSTEM', 'INFO', 'e' + i);
            }
            assert.strictEqual(s.getEvents().length, 5);
        });
    });

    describe('max sessions eviction', () => {
        it('evicts oldest sessions', () => {
            const small = createSessionLogger({ maxSessions: 3 });
            small.startSession({ printJobId: 'first' });
            small.startSession({ printJobId: 'second' });
            small.startSession({ printJobId: 'third' });
            small.startSession({ printJobId: 'fourth' });
            const list = small.listSessions();
            assert.strictEqual(list.length, 3);
            assert.ok(!list.some(s => s.meta.printJobId === 'first'));
        });
    });

    describe('constants exposed', () => {
        it('exposes CATEGORIES', () => {
            assert.ok(logger.CATEGORIES.SYSTEM);
            assert.ok(logger.CATEGORIES.NOZZLE);
            assert.ok(logger.CATEGORIES.SAFETY);
        });

        it('exposes SEVERITIES', () => {
            assert.ok(logger.SEVERITIES.DEBUG);
            assert.ok(logger.SEVERITIES.CRITICAL);
        });
    });
});
