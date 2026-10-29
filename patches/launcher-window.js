function __portArcadeApplyWindow(engine) {
  const raw = process.env.PORT_ARCADE_WINDOW_JSON;
  if (!raw) return;
  const input = JSON.parse(raw), window = {};
  for (const [key, min, max] of [['width',640,3840],['height',480,2160],['fontSize',10,32]]) {
    const value = Number(input[key]);
    if (Number.isInteger(value) && value >= min && value <= max) window[key] = value;
  }
  if (typeof input.autoMax === 'boolean') window.autoMax = input.autoMax;
  engine.config.window = {...engine.config.window, ...window};
  engine.resizeWindow();
  engine.connect('setConfig', {config:engine.config, fixed:engine.fixedConfig});
}
