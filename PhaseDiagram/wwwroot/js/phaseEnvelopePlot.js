window.phaseEnvelopePlot = {

    render: function (elementId, data, width, height) {
        var container = document.getElementById(elementId);
        if (!container) { console.warn('[PhasePlot] #' + elementId + ' not found'); return; }
        if (!data) { console.warn('[PhasePlot] null data'); return; }

        var traces = this._buildTraces(data);
        var layout = this._buildLayout(width, height);
        var config = this._buildConfig();

        // Find or create the iframe
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

        // Wait for iframe to load, then render
        var doRender = function () {
            if (iframe.contentWindow && iframe.contentWindow.renderPlot) {
                iframe.contentWindow.renderPlot(traces, layout, config);
            } else {
                // iframe not ready yet — retry
                setTimeout(doRender, 50);
            }
        };

        if (iframe.contentWindow && iframe.contentWindow.renderPlot) {
            // iframe already loaded (subsequent renders)
            doRender();
        } else {
            // First render — wait for load
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

    _buildTraces: function (data) {
        var traces = [];

        if (data.showRetrograde && data.critical &&
            data.dew.length > 1 && data.bubble.length > 1) {
            var tCrit = data.critical.t;
            var self = this;
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

        var bubbleX = data.bubble.map(function (p) { return p.t; });
        var bubbleY = data.bubble.map(function (p) { return p.p; });
        if (bubbleX.length > 0) {
            traces.push({
                x: bubbleX, y: bubbleY, mode: 'lines', type: 'scatter',
                name: 'Bubble Point (Q=0)', connectgaps: true,
                line: { color: 'rgb(214,39,40)', width: 2.5, shape: 'spline', smoothing: 0.8 }
            });
        }

        var dewX = data.dew.map(function (p) { return p.t; });
        var dewY = data.dew.map(function (p) { return p.p; });
        if (dewX.length > 0) {
            traces.push({
                x: dewX, y: dewY, mode: 'lines', type: 'scatter',
                name: 'Dew Point (Q=1)', connectgaps: true,
                line: { color: 'rgb(31,119,180)', width: 2.5, shape: 'spline', smoothing: 0.8 }
            });
        }

        var dropoutColors = ['#2ca02c', '#ff7f0e', '#d62728', '#9467bd'];
        var ci = 0;
        if (data.liquidLines && typeof data.liquidLines === 'object') {
            var entries = Object.entries(data.liquidLines);
            for (var k = 0; k < entries.length; k++) {
                var lf = entries[k][0], pts = entries[k][1];
                if (!pts || pts.length === 0) continue;
                var pct = (parseFloat(lf) * 100).toFixed(0);
                traces.push({
                    x: pts.map(function (p) { return p.t; }),
                    y: pts.map(function (p) { return p.p; }),
                    mode: 'lines', type: 'scatter',
                    name: pct + '% Liquid', connectgaps: true,
                    line: {
                        color: dropoutColors[ci++ % dropoutColors.length],
                        width: 2, dash: 'dash', shape: 'spline', smoothing: 0.6
                    }
                });
            }
        }

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