// Sample Dynamics web resource used by MinifyJS.Tests.ps1.
// Deliberately contains: console/debugger noise that gulp-strip-debug must
// remove, and ES6+ syntax that terser must preserve (the reason the task
// moved off gulp-uglify in the first place).

const formatGreeting = (name) => {
    console.log("this line must not survive minification");
    debugger;

    const who = name ?? "world";
    const parts = [ ...who.split(" ") ];

    return `Hello, ${parts.join(" ")}!`;
};

function onLoad(executionContext) {
    const formContext = executionContext?.getFormContext?.();
    if (formContext) {
        formContext.getAttribute("name")?.setValue(formatGreeting("Dynamics"));
    }
}

window.formatGreeting = formatGreeting;
window.onLoad = onLoad;
