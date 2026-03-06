window.phaseEnvelopePlot = {

    PLOT_W: 1025,
    PLOT_H: 575,

    render: function (elementId, data) {
        const el = document.getElementById(elementId);
        if (!el) return;

        if (el._fullLayout) {
            Plotly.purge(el);
        }

        el.style.cssText = [
            'width:' + this.PLOT_W + 'px',
            'height:' + this.PLOT_H + 'px',
            'max-width:' + this.PLOT_W + 'px',
            'max-height:' + this.PLOT_H + 'px',
            'overflow:hidden',
            'flex:none',
            'display:block'
        ].join(';');

        const traces = [];
        const bubbleX = data.bubble.map(p => p.t);
        const bubbleY = data.bubble.map(p => p.p);
        const dewX = data.dew.map(p => p.t);
        const dewY = data.dew.map(p => p.p);

        if (data.showRetrograde && bubbleX.length > 1 && dewX.length > 1) {
            const dewRevX = dewX.slice().reverse();
            const dewRevY = dewY.slice().reverse();
            traces.push({
                x: [...bubbleX, ...dewRevX.slice(1)],
                y: [...bubbleY, ...dewRevY.slice(1)],
                fill: 'toself', fillcolor: 'rgba(255,200,100,0.20)',
                type: 'scatter', mode: 'none',
                name: 'Two-Phase Region', showlegend: true, hoverinfo: 'skip'
            });
        }

        if (bubbleX.length > 0) {
            traces.push({
                x: bubbleX, y: bubbleY,
                mode: 'lines', type: 'scatter',
                name: 'Bubble Point (Q=0)', connectgaps: true,
                line: { color: 'rgb(214,39,40)', width: 2.5, shape: 'spline', smoothing: 0.8 }
            });
        }

        if (dewX.length > 0) {
            traces.push({
                x: dewX, y: dewY,
                mode: 'lines', type: 'scatter',
                name: 'Dew Point (Q=1)', connectgaps: true,
                line: { color: 'rgb(31,119,180)', width: 2.5, shape: 'spline', smoothing: 0.8 }
            });
        }

        const dropoutColors = ['#2ca02c', '#ff7f0e', '#d62728', '#9467bd'];
        let ci = 0;
        if (data.liquidLines && typeof data.liquidLines === 'object') {
            for (const [lf, pts] of Object.entries(data.liquidLines)) {
                if (pts && pts.length > 0) {
                    traces.push({
                        x: pts.map(p => p.t), y: pts.map(p => p.p),
                        mode: 'lines', type: 'scatter',
                        name: `${(parseFloat(lf) * 100).toFixed(0)}% Liquid`,
                        connectgaps: true,
                        line: {
                            color: dropoutColors[ci++ % dropoutColors.length],
                            width: 2, dash: 'dash', shape: 'spline', smoothing: 0.6
                        }
                    });
                }
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
                hovertemplate: `<b>Critical Point</b><br>T: ${data.critical.t.toFixed(1)} °F<br>P: ${data.critical.p.toFixed(0)} psia<extra></extra>`
            });
        }

        if (data.referencePoints && data.referencePoints.length > 0) {
            data.referencePoints.forEach(rp => {
                traces.push({
                    x: [rp.t], y: [rp.p],
                    mode: 'markers+text', type: 'scatter', name: rp.label,
                    marker: {
                        size: 12, color: rp.color, symbol: rp.symbol,
                        line: { color: 'black', width: 1 }
                    },
                    text: [rp.label], textposition: 'top center',
                    hovertemplate: `<b>${rp.label}</b><br>T: ${rp.t.toFixed(1)} °F<br>P: ${rp.p.toFixed(0)} psia<extra></extra>`
                });
            });
        }

        const layout = {
            width: this.PLOT_W,
            height: this.PLOT_H,
            margin: { l: 90, r: 30, t: 40, b: 70, pad: 4 },
            xaxis: {
                title: { text: 'Temperature (°F)', font: { size: 13, color: '#333' }, standoff: 12 },
                automargin: true,
                gridcolor: '#e6e6e6', showline: true, linewidth: 1,
                linecolor: '#aaa', mirror: true, zeroline: false, ticks: 'outside'
            },
            yaxis: {
                title: { text: 'Pressure (psia)', font: { size: 13, color: '#333' }, standoff: 12 },
                automargin: true,
                gridcolor: '#e6e6e6', showline: true, linewidth: 1,
                linecolor: '#aaa', mirror: true, zeroline: false,
                rangemode: 'tozero', ticks: 'outside'
            },
            plot_bgcolor: 'rgb(250,250,250)', paper_bgcolor: 'white',
            hovermode: 'closest', showlegend: true,
            legend: {
                orientation: 'v',
                x: 0.02, xanchor: 'left',
                y: 0.98, yanchor: 'top',
                bgcolor: 'rgba(255,255,255,0.88)', bordercolor: '#ccc',
                borderwidth: 1, font: { size: 11 }
            }
        };

        Plotly.newPlot(elementId, traces, layout, {
            responsive: false,
            displayModeBar: true,
            displaylogo: false,
            modeBarButtonsToRemove: ['lasso2d', 'select2d'],
            toImageButtonOptions: {
                format: 'png', filename: 'phase_envelope',
                height: 800, width: 1400, scale: 2
            }
        });
    }
};