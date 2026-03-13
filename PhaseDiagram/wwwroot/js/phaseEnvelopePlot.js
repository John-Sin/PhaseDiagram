window.phaseEnvelopePlot = {

    render: function (elementId, data, width, height) {
        var container = document.getElementById(elementId);
        if (!container) { console.warn('[PhasePlot] #' + elementId + ' not found'); return; }
        if (!data) { console.warn('[PhasePlot] null data'); return; }

        var traces = this._buildTraces(data);
        var layout = this._buildLayout(width, height, data);
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

    _getTheme: function (dark) {
        if (dark) {
            return {
                plotBg: '#2A2A2A',
                paperBg: '#1E1E1E',
                gridColor: '#444',
                gridOpacity: 0.5,
                gridWidth: 0.6,
                lineColor: '#666',
                fontColor: '#E6E6E6',
                tickColor: '#E6E6E6',
                legendBg: 'rgba(30,30,30,0.92)',
                legendBorder: '#444',
                twoPhaseFill: 'rgba(88, 91, 112, 0.30)',
                retroFill: 'rgba(204, 163, 0, 0.30)',
                retroLine: 'rgba(204, 163, 0, 0.50)',
                annotBg: 'rgba(30,30,30,0.90)',
                annotBorder: '#444',
                annotFont: '#E6E6E6',
                borderColor: '#444',
                bubbleColor: '#FF6B6B',
                dewColor: '#4EA8DE'
            };
        }
        return {
            plotBg: '#ECEFF3',
            paperBg: '#ECEFF3',
            gridColor: '#D8DCE3',
            gridOpacity: 0.5,
            gridWidth: 0.6,
            lineColor: '#666',
            fontColor: '#2C2C2C',
            tickColor: '#444',
            legendBg: 'rgba(255,255,255,0.85)',
            legendBorder: '#D0D4DA',
            twoPhaseFill: 'rgba(210, 210, 210, 0.35)',
            retroFill: 'rgba(184, 148, 0, 0.40)',
            retroLine: 'rgba(184, 148, 0, 0.55)',
            annotBg: 'rgba(255,255,255,0.85)',
            annotBorder: '#D0D4DA',
            annotFont: '#2C2C2C',
            borderColor: '#C8CDD6',
            bubbleColor: '#C94F4F',
            dewColor: '#2E6DA4'
        };
    },

    _buildLayout: function (width, height, data) {
        var theme = this._getTheme(data && data.darkMode);

        var layout = {
            width: width,
            height: height,
            autosize: false,
            margin: { l: 72, r: 25, t: 30, b: 62, pad: 4 },
            xaxis: {
                title: { text: 'Temperature (\u00B0F)', font: { size: 14, color: theme.fontColor } },
                gridcolor: theme.gridColor, gridwidth: theme.gridWidth,
                showline: true, linewidth: 1, linecolor: theme.borderColor,
                mirror: true, zeroline: false, ticks: 'outside',
                tickfont: { size: 12, color: theme.tickColor }
            },
            yaxis: {
                title: { text: 'Pressure (psia)', font: { size: 14, color: theme.fontColor } },
                gridcolor: theme.gridColor, gridwidth: theme.gridWidth,
                showline: true, linewidth: 1, linecolor: theme.borderColor,
                mirror: true, zeroline: false, rangemode: 'tozero',
                ticks: 'outside', tickfont: { size: 12, color: theme.tickColor }
            },
            plot_bgcolor: theme.plotBg,
            paper_bgcolor: theme.paperBg,
            hovermode: 'closest',
            showlegend: true,
            legend: {
                orientation: 'v',
                x: 0.01, xanchor: 'left',
                y: 0.99, yanchor: 'top',
                bgcolor: theme.legendBg,
                bordercolor: theme.legendBorder, borderwidth: 1,
                font: { size: 12, color: theme.fontColor }
            },
            title: {
                font: { size: 18, color: theme.fontColor }
            },
            annotations: []
        };

        if (data) {
            if (data.xMin != null && data.xMax != null) {
                layout.xaxis.range = [data.xMin, data.xMax];
            }
            if (data.yMax != null) {
                layout.yaxis.range = [0, data.yMax];
            }
        }

        // ── Critical point label box above the CP marker ──
        if (data && data.critical) {
            var ct = this._T(data.critical);
            var cp = this._P(data.critical);
            if (ct !== undefined && cp !== undefined) {
                layout.annotations.push({
                    x: ct,
                    y: cp,
                    xref: 'x',
                    yref: 'y',
                    text: '<b>Critical Point</b><br>'
                        + 'T<sub>c</sub> = ' + ct.toFixed(1) + ' \u00B0F<br>'
                        + 'P<sub>c</sub> = ' + cp.toFixed(0) + ' psia',
                    showarrow: false,
                    ax: 0,
                    ay: -108,
                    bordercolor: theme.annotBorder,
                    borderwidth: 1.5,
                    borderpad: 6,
                    bgcolor: theme.annotBg,
                    font: {
                        size: 11,
                        color: theme.annotFont
                    },
                    align: 'center',
                    yshift: 108
                });
            }
        }


        return layout;
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

    _T: function (pt) {
        if (pt.t !== undefined) return pt.t;
        if (pt.T !== undefined) return pt.T;
        return undefined;
    },
    _P: function (pt) {
        if (pt.p !== undefined) return pt.p;
        if (pt.P !== undefined) return pt.P;
        return undefined;
    },

    _buildTraces: function (data) {
        var traces = [];
        var self = this;
        var theme = this._getTheme(data && data.darkMode);

        // ── Two-phase region shading (pre-computed in C#) ──
        var tp = data.twoPhasePolygon;
        if (tp && tp.x && tp.x.length > 2) {
            traces.push({
                x: tp.x, y: tp.y,
                type: 'scatter', mode: 'none',
                fill: 'toself',
                fillcolor: theme.twoPhaseFill,
                line: { color: 'rgba(0,0,0,0)', width: 0 },
                name: 'Two-Phase Region',
                showlegend: false,
                hoverinfo: 'skip'
            });
        }

        // ── Retrograde region shading (pre-computed in C#) ──
        var rp = data.retrogradePolygon;
        if (data.showRetrograde && rp && rp.x && rp.x.length > 2) {
            traces.push({
                x: rp.x, y: rp.y,
                type: 'scatter', mode: 'none',
                fill: 'toself',
                fillcolor: theme.retroFill,
                line: { color: theme.retroLine, width: 0.5 },
                name: 'Retrograde Region',
                showlegend: true,
                hoverinfo: 'skip'
            });
        }

        // ── Bubble curve ──
        if (data.bubble && data.bubble.length > 0) {
            traces.push({
                x: data.bubble.map(function (p) { return self._T(p); }),
                y: data.bubble.map(function (p) { return self._P(p); }),
                mode: 'lines', type: 'scatter',
                name: 'Bubble Point (Q=0)', connectgaps: true,
                line: { color: theme.bubbleColor, width: 3, shape: 'spline', smoothing: 0.8 }
            });
        }

        // ── Dew curve ──
        if (data.dew && data.dew.length > 0) {
            traces.push({
                x: data.dew.map(function (p) { return self._T(p); }),
                y: data.dew.map(function (p) { return self._P(p); }),
                mode: 'lines', type: 'scatter',
                name: 'Dew Point (Q=1)', connectgaps: true,
                line: { color: theme.dewColor, width: 3, shape: 'spline', smoothing: 0.8 }
            });
        }

        // ── Liquid dropout contours ──
        var dropoutColors = data.darkMode
            ? ['#66bb6a', '#ffb74d', '#ef5350', '#ce93d8', '#a1887f', '#f06292', '#90a4ae', '#dce775']
            : ['#5B9F50', '#E6A23C', '#D9534F', '#7E57C2', '#6D4C41', '#c2185b', '#546e7a', '#9e9d24'];
        var ci = 0;

        if (data.liquidLines && typeof data.liquidLines === 'object') {
            var entries = Object.entries(data.liquidLines);
            entries.sort(function (a, b) { return parseFloat(a[0]) - parseFloat(b[0]); });

            for (var k = 0; k < entries.length; k++) {
                var lf = entries[k][0], pts = entries[k][1];
                if (!pts || pts.length < 3) continue;

                var pct = (parseFloat(lf) * 100).toFixed(0);

                traces.push({
                    x: pts.map(function (p) { return self._T(p); }),
                    y: pts.map(function (p) { return self._P(p); }),
                    mode: 'lines', type: 'scatter',
                    name: pct + '% Liquid', connectgaps: true,
                    line: {
                        color: dropoutColors[ci++ % dropoutColors.length],
                        width: 2.2, dash: 'dash',
                        shape: 'spline', smoothing: 1.0
                    }
                });
            }
        }

        // ── Critical point marker ──
        if (data.critical) {
            var ct = self._T(data.critical), cp = self._P(data.critical);
            traces.push({
                x: [ct], y: [cp],
                mode: 'markers', type: 'scatter', name: 'Critical Point',
                marker: {
                    size: 14, color: data.darkMode ? 'rgb(102,255,102)' : 'rgb(34,139,34)', symbol: 'star',
                    line: { color: data.darkMode ? 'rgb(200,255,200)' : 'rgb(0,60,0)', width: 2 }
                },
                hovertemplate: '<b>Critical Point</b><br>T: ' + ct.toFixed(1) +
                    ' \u00B0F<br>P: ' + cp.toFixed(0) + ' psia<extra></extra>'
            });
        }

        // ── Reference points ──
        if (data.referencePoints && data.referencePoints.length > 0) {
            data.referencePoints.forEach(function (rp) {
                var rt = self._T(rp), rpp = self._P(rp);
                traces.push({
                    x: [rt], y: [rpp], mode: 'markers+text', type: 'scatter',
                    name: rp.label,
                    marker: {
                        size: 12, color: rp.color, symbol: rp.symbol,
                        line: { color: data.darkMode ? 'white' : 'black', width: 1 }
                    },
                    text: [rp.label], textposition: 'top center',
                    textfont: { color: theme.fontColor },
                    hovertemplate: '<b>' + rp.label + '</b><br>T: ' + rt.toFixed(1) +
                        ' \u00B0F<br>P: ' + rpp.toFixed(0) + ' psia<extra></extra>'
                });
            });
        }

        return traces;
    }
};