export async function onRuntimeConfigLoaded(config) {
    const source = new URL('_blazor/_configuration', document.baseURI).href;
    const response = await fetch(source);
    if (!response.ok) return;

    const json = await response.json();
    const environment = json?.webAssembly?.environment;
    if (!environment) return;

    Object.assign(config.environmentVariables ??= {}, environment);
}