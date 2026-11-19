
void main()
{
    if (!krs_water_place())
    {
        gl_Position = vec4(0.0, 0.0, -2.0, 1.0);
        return;
    }
    krs_game_water_main();
}
