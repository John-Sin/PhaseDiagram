window.phaseEnvelopePlot = {

    render: function (elementId, data, width, height) {
        var container = document.getElementById(elementId);
        if (!container) { console.warn('[PhasePlot] #' + elementId + ' not found'); return; }
        if (!data) { console.warn('[PhasePlot] null data'); return; }

        var traces = this._buildTraces(data);
        var layout = this._buildLayout(width, height);
        var config = this._buildConfig();

        var iframe = container.querySelector('iframe');
        if (!iframe) {
            iframe = document.createElement('iframe');
            iframe.src = 'plotly-frame.html';
            iframe.style.width = width + 'px';
            iframe.style.height = height + 'px';
            iframe.style.border = 'none';
            iframe.style.display = 'block';
            container.innerHTML = '';
            container.appendChild(iframe);
        }

        var doRender = function () {
            if (iframe.contentWindow && iframe.contentWindow.renderPlot) {
                iframe.contentWindow.renderPlot(traces, layout, config);
            } else {
                setTimeout(doRender, 50);
            }
        };

        if (iframe.contentWindow && iframe.contentWindow.renderPlot) {
            doRender();
        } else {
            iframe.onload = doRender;
        }
    },

    _buildLayout: function (width, height) {
        return {
            width: width,
            height: height,
            autosize: false,
            margin: { l: 70, r: 25, t: 30, b: 60, pad: 4 },
            xaxis: {
                title: 'Temperature (\u00B0F)',
                gridcolor: '#e6e6e6', showline: true, linewidth: 1,
                linecolor: '#aaa', mirror: true, zeroline: false, ticks: 'outside'
            },
            yaxis: {
                title: 'Pressure (psia)',
                gridcolor: '#e6e6e6', showline: true, linewidth: 1,
                linecolor: '#aaa', mirror: true, zeroline: false,
                rangemode: 'tozero', ticks: 'outside'
            },
            plot_bgcolor: 'rgb(250,250,250)',
            paper_bgcolor: 'white',
            hovermode: 'closest',
            showlegend: true,
            legend: {
                orientation: 'v',
                x: 0.01, xanchor: 'left',
                y: 0.99, yanchor: 'top',
                bgcolor: 'rgba(255,255,255,0.88)',
                bordercolor: '#ccc', borderwidth: 1,
                font: { size: 11 }
            }
        };
    },

    _buildConfig: function () {
        return {
            responsive: false,
            displayModeBar: true,
            displaylogo: false,
            modeBarButtonsToRemove: ['lasso2d', 'select2d'],
            toImageButtonOptions: {
                format: 'png', filename: 'phase_envelope',
                height: 960, width: 1680, scale: 2
            }
        };
    },

    _sortedMono: function (pts) {
        if (!pts || pts.length === 0) return [];
        var sorted = pts.slice().sort(function (a, b) { return a.t - b.t; });
        var out = [sorted[0]];
        for (var i = 1; i < sorted.length; i++) {
            if (sorted[i].t > out[out.length - 1].t) out.push(sorted[i]);
        }
        return out;
    },

    _lerpP: function (curve, t) {
        if (curve.length === 0) return null;
        if (t < curve[0].t || t > curve[curve.length - 1].t) return null;
        if (curve.length === 1) return curve[0].p;
        for (var i = 0; i < curve.length - 1; i++) {
            if (t >= curve[i].t && t <= curve[i + 1].t) {
                var f = (t - curve[i].t) / (curve[i + 1].t - curve[i].t);
                return curve[i].p + f * (curve[i + 1].p - curve[i].p);
            }
        }
        return curve[curve.length - 1].p;
    },

    /// Find the MAXIMUM T on a curve at a given pressure.
    _curveMaxT: function (curvePts, p) {
        if (!curvePts || curvePts.length < 2) return null;
        var maxT = null;
        for (var i = 0; i < curvePts.length - 1; i++) {
            var p0 = curvePts[i].p, p1 = curvePts[i + 1].p;
            var pMin = Math.min(p0, p1), pMax = Math.max(p0, p1);
            if (p < pMin || p > pMax) continue;
            var denom = p1 - p0;
            if (Math.abs(denom) < 1e-10) continue;
            var frac = (p - p0) / denom;
            var t = curvePts[i].t + frac * (curvePts[i + 1].t - curvePts[i].t);
            if (maxT === null || t > maxT) maxT = t;
        }
        return maxT;
    },

    /// Find the MINIMUM T on a curve at a given pressure.
    _curveMinT: function (curvePts, p) {
        if (!curvePts || curvePts.length < 2) return null;
        var minT = null;
        for (var i = 0; i < curvePts.length - 1; i++) {
            var p0 = curvePts[i].p, p1 = curvePts[i + 1].p;
            var pMin = Math.min(p0, p1), pMax = Math.max(p0, p1);
            if (p < pMin || p > pMax) continue;
            var denom = p1 - p0;
            if (Math.abs(denom) < 1e-10) continue;
            var frac = (p - p0) / denom;
            var t = curvePts[i].t + frac * (curvePts[i + 1].t - curvePts[i].t);
            if (minT === null || t < minT) minT = t;
        }
        return minT;
    },

    /// True right (high-T) envelope boundary at a given pressure.
    _envelopeMaxT: function (bubblePts, dewPts, p) {
        var maxBub = this._curveMaxT(bubblePts, p);
        var maxDew = this._curveMaxT(dewPts, p);
        if (maxBub !== null && maxDew !== null) return Math.max(maxBub, maxDew);
        if (maxBub !== null) return maxBub;
        return maxDew;
    },

    /// True left (low-T) envelope boundary at a given pressure.
    _envelopeMinT: function (bubblePts, dewPts, p) {
        var minBub = this._curveMinT(bubblePts, p);
        var minDew = this._curveMinT(dewPts, p);
        if (minBub !== null && minDew !== null) return Math.min(minBub, minDew);
        if (minBub !== null) return minBub;
        return minDew;
    },

    /// Centripetal Catmull-Rom spline interpolation.
    _catmullRom: function (pts, totalPts) {
        if (!pts || pts.length < 2) return pts ? pts.slice() : [];
        if (pts.length === 2) {
            var out2 = [];
            for (var s = 0; s < totalPts; s++) {
                var f2 = s / (totalPts - 1);
                out2.push({
                    t: pts[0].t + f2 * (pts[1].t - pts[0].t),
                    p: pts[0].p + f2 * (pts[1].p - pts[0].p)
                });
            }
            return out2;
        }

        var n = pts.length;
        var d = [0];
        for (var i = 1; i < n; i++) {
            var dx = pts[i].t - pts[i - 1].t;
            var dy = pts[i].p - pts[i - 1].p;
            d.push(d[i - 1] + Math.sqrt(Math.sqrt(dx * dx + dy * dy)));
        }
        var totalLen = d[n - 1];
        if (totalLen <= 0) return pts.slice();

        var out = [];
        var seg = 0;

        for (var si = 0; si < totalPts; si++) {
            var u = (si / (totalPts - 1)) * totalLen;
            while (seg < n - 2 && d[seg + 1] < u) seg++;

            var i0 = Math.max(seg - 1, 0);
            var i1 = seg;
            var i2 = Math.min(seg + 1, n - 1);
            var i3 = Math.min(seg + 2, n - 1);

            var t0 = d[i0], t1 = d[i1], t2 = d[i2], t3 = d[i3];
            var segLen = t2 - t1;
            var localU = segLen > 0 ? (u - t1) / segLen : 0;
            localU = Math.max(0, Math.min(1, localU));

            var d0 = t1 - t0 > 0 ? t1 - t0 : 1;
            var d1 = t2 - t1 > 0 ? t2 - t1 : 1;
            var d2 = t3 - t2 > 0 ? t3 - t2 : 1;

            var crInterp = function (v0, v1, v2, v3, uu) {
                var m1 = (v2 - v1) + d1 * ((v1 - v0) / d0 - (v2 - v0) / (d0 + d1));
                var m2 = (v2 - v1) + d1 * ((v3 - v2) / d2 - (v3 - v1) / (d1 + d2));
                var a = 2 * v1 - 2 * v2 + m1 + m2;
                var b = -3 * v1 + 3 * v2 - 2 * m1 - m2;
                return a * uu * uu * uu + b * uu * uu + m1 * uu + v1;
            };

            out.push({
                t: crInterp(pts[i0].t, pts[i1].t, pts[i2].t, pts[i3].t, localU),
                p: crInterp(pts[i0].p, pts[i1].p, pts[i2].p, pts[i3].p, localU)
            });
        }
        return out;
    },

    _buildTraces: function (data) {
        var traces = [];
        var self = this;

        // ── Retrograde shading ──
        if (data.showRetrograde && data.critical &&
            data.dew.length > 1 && data.bubble.length > 1) {
            var tCrit = data.critical.t;
            var bubMono = this._sortedMono(data.bubble);
            var dewMono = this._sortedMono(data.dew);
            var tLo = Math.max(tCrit, bubMono[0].t, dewMono[0].t);
            var tHi = Math.min(bubMono[bubMono.length - 1].t, dewMono[dewMono.length - 1].t);
            if (tHi > tLo + 0.1) {
                var N = 200, step = (tHi - tLo) / (N - 1);
                var polyX = [], polyY = [];
                for (var i = 0; i < N; i++) {
                    var t = tLo + i * step;
                    var pBub = self._lerpP(bubMono, t);
                    if (pBub !== null) { polyX.push(t); polyY.push(pBub); }
                }
                for (var j = N - 1; j >= 0; j--) {
                    var t2 = tLo + j * step;
                    var pDew = self._lerpP(dewMono, t2);
                    if (pDew !== null) { polyX.push(t2); polyY.push(pDew); }
                }
                if (polyX.length > 4) {
                    polyX.push(polyX[0]); polyY.push(polyY[0]);
                    traces.push({
                        x: polyX, y: polyY, type: 'scatter', mode: 'none',
                        fill: 'toself', fillcolor: 'rgba(255,200,100,0.25)',
                        line: { color: 'rgba(0,0,0,0)', width: 0 },
                        name: 'Retrograde Region', showlegend: true, hoverinfo: 'skip'
                    });
                }
            }
        }

        // ── Bubble curve ──
        var bubbleX = data.bubble.map(function (p) { return p.t; });
        var bubbleY = data.bubble.map(function (p) { return p.p; });
        if (bubbleX.length > 0) {
            traces.push({
                x: bubbleX, y: bubbleY, mode: 'lines', type: 'scatter',
                name: 'Bubble Point (Q=0)', connectgaps: true,
                line: { color: 'rgb(214,39,40)', width: 2.5, shape: 'spline', smoothing: 0.8 }
            });
        }

        // ── Dew curve ──
        var dewX = data.dew.map(function (p) { return p.t; });
        var dewY = data.dew.map(function (p) { return p.p; });
        if (dewX.length > 0) {
            traces.push({
                x: dewX, y: dewY, mode: 'lines', type: 'scatter',
                name: 'Dew Point (Q=1)', connectgaps: true,
                line: { color: 'rgb(31,119,180)', width: 2.5, shape: 'spline', smoothing: 0.8 }
            });
        }

        // ── Liquid dropout lines ──
        var pCrit = data.critical ? data.critical.p : null;
        var tCrit = data.critical ? data.critical.t : null;

        var dropoutColors = ['#2ca02c', '#ff7f0e', '#d62728', '#9467bd',
            '#8c564b', '#e377c2', '#7f7f7f', '#bcbd22'];
        var ci = 0;
        if (data.liquidLines && typeof data.liquidLines === 'object') {

            // ── Compute cricondentherm: absolute max T across both curves ──
            // No dropout point should ever exceed this temperature.
            var tMax = null;
            for (var bi = 0; bi < data.bubble.length; bi++) {
                if (tMax === null || data.bubble[bi].t > tMax) tMax = data.bubble[bi].t;
            }
            for (var di = 0; di < data.dew.length; di++) {
                if (tMax === null || data.dew[di].t > tMax) tMax = data.dew[di].t;
            }

            var entries = Object.entries(data.liquidLines);
            for (var k = 0; k < entries.length; k++) {
                var lf = entries[k][0], pts = entries[k][1];
                if (!pts || pts.length < 2) continue;
                var pct = (parseFloat(lf) * 100).toFixed(0);

                // ── SKIP Catmull-Rom — use raw points directly ──
                // The server now sends enough data (with smooth approach-
                // to-CP points) that the Catmull-Rom spline just causes
                // overshoot artifacts. Let Plotly's built-in spline
                // handle the visual smoothing instead.
                var clamped = [];
                for (var si = 0; si < pts.length; si++) {
                    var pt = pts[si];

                    // 1. Pressure must not exceed Pcrit.
                    if (pCrit !== null && pt.p > pCrit * 0.999) continue;

                    // 2. Pressure must be positive.
                    if (pt.p <= 0) continue;

                    // 3. Hard cricondentherm limit — catches spline overshoot.
                    if (tMax !== null && pt.t > tMax) continue;

                    // 4. T must stay inside the true envelope boundaries.
                    var rightBound = self._envelopeMaxT(data.bubble, data.dew, pt.p);
                    var leftBound = self._envelopeMinT(data.bubble, data.dew, pt.p);

                    if (rightBound !== null && leftBound !== null) {
                        var envelopeWidth = rightBound - leftBound;
                        var margin = Math.min(0.3, envelopeWidth * 0.02);

                        var t = pt.t;
                        if (t > rightBound - margin) t = rightBound - margin;
                        if (t < leftBound + margin) t = leftBound + margin;

                        if (t > leftBound && t < rightBound) {
                            clamped.push({ t: t, p: pt.p });
                        }
                    } else {
                        // Can't determine envelope bounds at this P —
                        // accept the point if it's within cricondentherm.
                        clamped.push({ t: pt.t, p: pt.p });
                    }
                }

                if (clamped.length < 2) continue;

                traces.push({
                    x: clamped.map(function (p) { return p.t; }),
                    y: clamped.map(function (p) { return p.p; }),
                    mode: 'lines', type: 'scatter',
                    name: pct + '% Liquid', connectgaps: true,
                    line: {
                        color: dropoutColors[ci++ % dropoutColors.length],
                        width: 2, dash: 'dash',
                        shape: 'spline', smoothing: 1.0
                    }
                });
            }
        }

        // ── Critical point marker ──
        if (data.critical) {
            traces.push({
                x: [data.critical.t], y: [data.critical.p],
                mode: 'markers', type: 'scatter', name: 'Critical Point',
                marker: {
                    size: 13, color: 'rgb(44,160,44)', symbol: 'star',
                    line: { color: 'rgb(0,80,0)', width: 1.5 }
                },
                hovertemplate: '<b>Critical Point</b><br>T: ' + data.critical.t.toFixed(1) +
                    ' \u00B0F<br>P: ' + data.critical.p.toFixed(0) + ' psia<extra></extra>'
            });
        }

        // ── Reference points ──
        if (data.referencePoints && data.referencePoints.length > 0) {
            data.referencePoints.forEach(function (rp) {
                traces.push({
                    x: [rp.t], y: [rp.p], mode: 'markers+text', type: 'scatter',
                    name: rp.label,
                    marker: {
                        size: 12, color: rp.color, symbol: rp.symbol,
                        line: { color: 'black', width: 1 }
                    },
                    text: [rp.label], textposition: 'top center',
                    hovertemplate: '<b>' + rp.label + '</b><br>T: ' + rp.t.toFixed(1) +
                        ' \u00B0F<br>P: ' + rp.p.toFixed(0) + ' psia<extra></extra>'
                });
            });
        }

        return traces;
    }
};