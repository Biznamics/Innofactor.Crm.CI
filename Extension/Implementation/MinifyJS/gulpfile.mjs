import gulp from 'gulp';
import terser from 'gulp-terser';
import strip from 'gulp-strip-debug';
import rename from 'gulp-rename';
import pluginError from 'plugin-error';

const pattern = function (file) {
  file.basename = file.basename.replace('.maxi', '');
};

export async function minify() {
  return gulp
    .src('*.maxi.js')
    .pipe(strip())
    .pipe(
      terser().on('error', function (uglify) {
        const err = new pluginError('minifyJS', uglify.message, {
          showStack: true,
        });
        this.emit('error', err);
      })
    )
    .pipe(rename(pattern))
    .pipe(gulp.dest('.'));
}
