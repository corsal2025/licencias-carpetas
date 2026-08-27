using LicenciasCarpetas.Domain;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Persistence;

public interface IComunaContactRepository
{
    void EnsureSchema();
    void EnsureSeed(string? csvPath = null);
    void Upsert(ComunaContact contact);
    void Update(long id, string comuna, string email, string? notes = null);
    IReadOnlyList<ComunaContact> All(string? search = null);
    void Delete(long id);
}

public sealed class ComunaContactRepository(string connectionString) : IComunaContactRepository
{
    public void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ComunaContact (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Comuna TEXT NOT NULL,
                Email TEXT NOT NULL,
                Notes TEXT NULL,
                UNIQUE (Comuna, Email)
            );
            """;
        command.ExecuteNonQuery();
    }

    public void EnsureSeed(string? csvPath = null)
    {
        var path = csvPath;
        if (string.IsNullOrEmpty(path))
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "data", "comunas.csv"),
                Path.Combine(Directory.GetCurrentDirectory(), "data", "comunas.csv"),
                Path.Combine(AppContext.BaseDirectory, "comunas.csv")
            };
            path = candidates.FirstOrDefault(File.Exists);
        }

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            var lines = File.ReadAllLines(path);
            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    var comuna = parts[0].Trim().Trim('"');
                    var email = parts[1].Trim().Trim('"');
                    if (comuna.Length > 0 && email.Contains('@'))
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = """
                            INSERT INTO ComunaContact (Comuna, Email, Notes)
                            VALUES ($comuna, $email, $notes)
                            ON CONFLICT(Comuna, Email) DO NOTHING;
                            """;
                        cmd.Parameters.AddWithValue("$comuna", comuna);
                        cmd.Parameters.AddWithValue("$email", email);
                        cmd.Parameters.AddWithValue("$notes", "Directorio oficial");
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            tx.Commit();
            return;
        }

        using var conn = Open();
        SeedDefaultContacts(conn);
    }

    private static void SeedDefaultContacts(SqliteConnection connection)
    {
        var defaultList = new (string Comuna, string Email)[]
        {
            // Región de Arica y Parinacota
            ("ARICA", "licencias@municipalidadarica.cl"),
            ("CAMARONES", "licencias@municamarones.cl"),
            ("GENERAL LAGOS", "licencias@munigenerallagos.cl"),
            ("PUTRE", "licencias@muniputre.cl"),

            // Región de Tarapacá
            ("ALTO HOSPICIO", "licencias@maho.cl"),
            ("CAMIÑA", "licencias@municamina.cl"),
            ("COLCHANE", "licencias@municipalidadcolchane.cl"),
            ("HUARA", "licencias@munihuara.cl"),
            ("IQUIQUE", "licencias@municipioiquique.cl"),
            ("PICA", "licencias@munipica.cl"),
            ("POZO ALMONTE", "licencias@munipozoalmonte.cl"),

            // Región de Antofagasta
            ("ANTOFAGASTA", "licencias@municipalidadantofagasta.cl"),
            ("CALAMA", "licencias@municipalidadcalama.cl"),
            ("MARÍA ELENA", "licencias@mme.cl"),
            ("MEJILLONES", "licencias@mejillones.cl"),
            ("OLLAGÜE", "licencias@municipalidadoallague.cl"),
            ("SAN PEDRO DE ATACAMA", "licencias@munispa.cl"),
            ("SIERRA GORDA", "licencias@munisierragorda.cl"),
            ("TALTAL", "licencias@taltal.cl"),
            ("TOCOPILLA", "licencias@municipalidadtocopilla.cl"),

            // Región de Atacama
            ("ALTO DEL CARMEN", "licencias@munialtodelcarmen.cl"),
            ("CALDERA", "licencias@caldera.cl"),
            ("CHAÑARAL", "licencias@munichanaral.cl"),
            ("COPIAPÓ", "licencias@copiapo.cl"),
            ("DIEGO DE ALMAGRO", "licencias@mdda.cl"),
            ("FREIRINA", "licencias@imfreirina.cl"),
            ("HUASCO", "licencias@municipalidadhuasco.cl"),
            ("TIERRA AMARILLA", "licencias@munita.cl"),
            ("VALLENAR", "licencias@vallenar.cl"),

            // Región de Coquimbo
            ("ANDACOLLO", "licencias@municipalidadandacollo.cl"),
            ("CANELA", "licencias@canela.cl"),
            ("COMBARBALÁ", "licencias@combarbala.cl"),
            ("COQUIMBO", "licencias@municoquimbo.cl"),
            ("ILLAPEL", "licencias@municipalidadillapel.cl"),
            ("LA HIGUERA", "licencias@munilahiguera.cl"),
            ("LA SERENA", "licencias@laserena.cl"),
            ("LOS VILOS", "licencias@munilosvilos.cl"),
            ("MONTE PATRIA", "licencias@mpatria.cl"),
            ("OVALLE", "licencias@municipalidaddeovalle.cl"),
            ("PAIHUANO", "licencias@munipaihuano.cl"),
            ("PUNITAQUI", "licencias@munipunitaqui.cl"),
            ("RÍO HURTADO", "licencias@riohurtado.cl"),
            ("SALAMANCA", "licencias@salamanca.cl"),
            ("VICUÑA", "licencias@munivicuna.cl"),

            // Región de Valparaíso
            ("ALGARROBO", "licencias@munialgarrobo.cl"),
            ("CABILDO", "licencias@municabildo.cl"),
            ("CALLE LARGA", "licencias@municallelarga.cl"),
            ("CARTAGENA", "licencias@municartagena.cl"),
            ("CASABLANCA", "licencias@municipalidadcasablanca.cl"),
            ("CATEMU", "licencias@municatemu.cl"),
            ("CONCÓN", "licencias@concon.cl"),
            ("EL QUISCO", "licencias@elquisco.cl"),
            ("EL TABO", "licencias@eltabo.cl"),
            ("HIJUELAS", "licencias@hijuelas.cl"),
            ("ISLA DE PASCUA", "licencias@rapanui.net"),
            ("JUAN FERNÁNDEZ", "licencias@comunajuanfernandez.cl"),
            ("LA CALERA", "licencias@lacalera.cl"),
            ("LA CRUZ", "licencias@lacruz.cl"),
            ("LA LIGUA", "licencias@laligua.cl"),
            ("LIMACHE", "licencias@munilimache.cl"),
            ("LLAILLAY", "licencias@munillayllay.cl"),
            ("LOS ANDES", "licencias@losandes.cl"),
            ("NOGALES", "licencias@muninogales.cl"),
            ("OLMUÉ", "licencias@muniolmue.cl"),
            ("PANQUEHUE", "licencias@panquehue.cl"),
            ("PAPUDO", "licencias@papudo.cl"),
            ("PETORCA", "licencias@munipetorca.cl"),
            ("PUCHUNCAVÍ", "licencias@munipuchuncavi.cl"),
            ("PUTAENDO", "licencias@putaendo.cl"),
            ("QUILLOTA", "licencias@quillota.cl"),
            ("QUILPUÉ", "licencias@quilpue.cl"),
            ("QUINTERO", "licencias@muniquintero.cl"),
            ("RINCONADA", "licencias@munirinconada.cl"),
            ("SAN ANTONIO", "licencias@sanantonio.cl"),
            ("SAN ESTEBAN", "licencias@munisanesteban.cl"),
            ("SAN FELIPE", "licencias@munisanfelipe.cl"),
            ("SANTA MARÍA", "licencias@imasantamaria.cl"),
            ("SANTO DOMINGO", "licencias@santodomingo.cl"),
            ("VALPARAÍSO", "licencias@munivalpo.cl"),
            ("VILLA ALEMANA", "licencias@villalemana.cl"),
            ("VIÑA DEL MAR", "licencias@vinadelmar.cl"),
            ("ZAPALLAR", "licencias@munizapallar.cl"),

            // Región Metropolitana de Santiago
            ("ALHUÉ", "licencias@comunadealhue.cl"),
            ("BUIN", "licencias@buin.cl"),
            ("CALERA DE TANGO", "licencias@caleradetango.cl"),
            ("CERRILLOS", "licencias@mcerrillos.cl"),
            ("CERRO NAVIA", "licencias@cerronavia.cl"),
            ("COLINA", "licencias@colina.cl"),
            ("CONCHALÍ", "licencias@conchali.cl"),
            ("CURACAVÍ", "licencias@municipalidadcuracavi.cl"),
            ("EL BOSQUE", "licencias@imelbosque.cl"),
            ("EL MONTE", "licencias@munielmonte.cl"),
            ("ESTACIÓN CENTRAL", "licencias@estacioncentral.cl"),
            ("HUECHURABA", "licencias@huechuraba.cl"),
            ("INDEPENDENCIA", "licencias@independencia.cl"),
            ("ISLA DE MAIPO", "licencias@islademaipo.cl"),
            ("LA CISTERNA", "licencias@lacisterna.cl"),
            ("LA FLORIDA", "licencias@laflorida.cl"),
            ("LA GRANJA", "licencias@lagranja.cl"),
            ("LA PINTANA", "licencias@pintana.cl"),
            ("LA REINA", "licencias@lareina.cl"),
            ("LAMPA", "licencias@lampa.cl"),
            ("LAS CONDES", "licencias@lascondes.cl"),
            ("LO BARNECHEA", "licencias@lobarnechea.cl"),
            ("LO ESPEJO", "licencias@loespejo.cl"),
            ("LO PRADO", "licencias@loprado.cl"),
            ("MACUL", "licencias@munimacul.cl"),
            ("MAIPÚ", "licencias@maipu.cl"),
            ("MARÍA PINTO", "licencias@mpinto.cl"),
            ("MELIPILLA", "licencias@melipilla.cl"),
            ("ÑUÑOA", "licencias@nunoa.cl"),
            ("PADRE HURTADO", "licencias@mph.cl"),
            ("PAINE", "licencias@paine.cl"),
            ("PEDRO AGUIRRE CERDA", "licencias@pedroaguirrecerda.cl"),
            ("PEÑAFLOR", "licencias@penaflor.cl"),
            ("PEÑALOLÉN", "licencias@penalolen.cl"),
            ("PIRQUE", "licencias@pirque.cl"),
            ("PROVIDENCIA", "licencias@providencia.cl"),
            ("PUDAHUEL", "licencias@mpudahuel.cl"),
            ("PUENTE ALTO", "licencias@mpuentealto.cl"),
            ("QUILICURA", "licencias@quilicura.cl"),
            ("QUINTA NORMAL", "licencias@quintanormal.cl"),
            ("RECOLETA", "licencias@recoleta.cl"),
            ("RENCA", "licencias@renca.cl"),
            ("SAN BERNARDO", "licencias@sanbernardo.cl"),
            ("SAN JOAQUÍN", "licencias@sanjoaquin.cl"),
            ("SAN JOSÉ DE MAIPO", "licencias@sanjosedemaipo.cl"),
            ("SAN MIGUEL", "licencias@sanmiguel.cl"),
            ("SAN PEDRO", "licencias@munisanpedro.cl"),
            ("SAN RAMÓN", "licencias@municipalidadsanramon.cl"),
            ("SANTIAGO", "licencias@munistgo.cl"),
            ("TALAGANTE", "licencias@talagante.cl"),
            ("TILTIL", "licencias@munitiltil.cl"),
            ("VITACURA", "licencias@vitacura.cl"),

            // Región de O'Higgins
            ("CHÉPICA", "licencias@municipalidadchepica.cl"),
            ("CHIMBARONGO", "licencias@chimbarongo.cl"),
            ("CODEGUA", "licencias@municipalidadcodegua.cl"),
            ("COINCO", "licencias@municoinco.cl"),
            ("COLTAUCO", "licencias@coltauco.cl"),
            ("DOÑIHUE", "licencias@donihue.cl"),
            ("GRANEROS", "licencias@graneros.cl"),
            ("LA ESTRELLA", "licencias@munilaestrella.cl"),
            ("LAS CABRAS", "licencias@lascabras.cl"),
            ("LITUECHE", "licencias@litueche.cl"),
            ("LOLOL", "licencias@munilolol.cl"),
            ("MACHALÍ", "licencias@machali.cl"),
            ("MALLOA", "licencias@malloa.cl"),
            ("MARCHIHUE", "licencias@marchigue.cl"),
            ("MOSTAZAL", "licencias@mostazal.cl"),
            ("NANCAGUA", "licencias@nancagua.cl"),
            ("NAVIDAD", "licencias@muninavidad.cl"),
            ("OLIVAR", "licencias@muniolivar.cl"),
            ("PALMILLA", "licencias@munipalmilla.cl"),
            ("PAREDONES", "licencias@paredones.cl"),
            ("PERALILLO", "licencias@muniperalillo.cl"),
            ("PEUMO", "licencias@munipeumo.cl"),
            ("PICHIDEGUA", "licencias@pichidegua.cl"),
            ("PICHILEMU", "licencias@pichilemu.cl"),
            ("PLACILLA", "licencias@muniplacilla.cl"),
            ("PUMANQUE", "licencias@pumanque.cl"),
            ("QUINTA DE TILCOCO", "licencias@quintadetilcoco.cl"),
            ("RANCAGUA", "licencias@rancagua.cl"),
            ("RENGO", "licencias@municipalidadrengo.cl"),
            ("REQUÍNOA", "licencias@requinoa.cl"),
            ("SAN FERNANDO", "licencias@munisanfernando.cl"),
            ("SAN VICENTE", "licencias@msanvicente.cl"),
            ("SANTA CRUZ", "licencias@municipalidadsantacruz.cl"),

            // Región del Maule
            ("CAUQUENES", "licencias@cauquenes.cl"),
            ("CHANCO", "licencias@munichanco.cl"),
            ("COLBÚN", "licencias@municipalidadcolbun.cl"),
            ("CONSTITUCIÓN", "licencias@constitucion.cl"),
            ("CUREPTO", "licencias@curepto.cl"),
            ("CURICÓ", "licencias@curico.cl"),
            ("EMPEDRADO", "licencias@municipioempedrado.cl"),
            ("HUALAÑÉ", "licencias@hualane.cl"),
            ("LICANTÉN", "licencias@mlicanten.cl"),
            ("LINARES", "licencias@munilinares.cl"),
            ("LONGAVÍ", "licencias@municipalidadlongavi.cl"),
            ("MAULE", "licencias@comunademaule.cl"),
            ("MOLINA", "licencias@molina.cl"),
            ("PARRAL", "licencias@parral.cl"),
            ("PELARCO", "licencias@pelarco.cl"),
            ("PELLUHUE", "licencias@munipelluhue.cl"),
            ("PENCAHUE", "licencias@mpencahue.cl"),
            ("RAUCO", "licencias@munirauco.cl"),
            ("RETIRO", "licencias@municipalidadretiro.cl"),
            ("RÍO CLARO", "licencias@municipalidadrioclaro.cl"),
            ("ROMERAL", "licencias@mromeral.cl"),
            ("SAGRADA FAMILIA", "licencias@sagradafamilia.cl"),
            ("SAN CLEMENTE", "licencias@sanclemente.cl"),
            ("SAN JAVIER", "licencias@consultassanjavier.cl"),
            ("SAN RAFAEL", "licencias@munisanrafael.cl"),
            ("TALCA", "licencias@talca.cl"),
            ("TENO", "licencias@teno.cl"),
            ("VICHUQUÉN", "licencias@muni-vichuquen.cl"),
            ("VILLA ALEGRE", "licencias@villalegre.cl"),
            ("YERBAS BUENAS", "licencias@muryerbasbuenas.cl"),

            // Región de Ñuble
            ("BULNES", "licencias@el培.cl"),
            ("CHILLÁN", "licencias@municipalidadchillan.cl"),
            ("CHILLÁN VIEJO", "licencias@chillanviejo.cl"),
            ("COBQUECURA", "licencias@cobquecura.cl"),
            ("COELEMU", "licencias@coelemu.cl"),
            ("COIHUECO", "licencias@municoihueco.cl"),
            ("EL CARMEN", "licencias@munielcarmen.cl"),
            ("NINHUE", "licencias@munininhue.cl"),
            ("ÑIQUÉN", "licencias@muniniquen.cl"),
            ("PEMUCO", "licencias@munipemuco.cl"),
            ("PINTO", "licencias@municipalidadpinto.cl"),
            ("PORTEZUELO", "licencias@municipalidadportezuelo.cl"),
            ("QUILLÓN", "licencias@quillon.cl"),
            ("QUIRIHUE", "licencias@quirihue.cl"),
            ("RÁNQUIL", "licencias@muniranquil.cl"),
            ("SAN CARLOS", "licencias@sancarlos.cl"),
            ("SAN FABIÁN", "licencias@sanfabian.cl"),
            ("SAN IGNACIO", "licencias@munisanignacio.cl"),
            ("SAN NICOLÁS", "licencias@munisannicolas.cl"),
            ("TREHUACO", "licencias@treguaco.cl"),
            ("YUNGAY", "licencias@yungay.cl"),

            // Región del Biobío
            ("ALTO BIOBÍO", "licencias@munialtobiobio.cl"),
            ("ANTUCO", "licencias@municipalidadantuco.cl"),
            ("ARAUCO", "licencias@muniarauco.cl"),
            ("CABRERO", "licencias@cabrero.cl"),
            ("CAÑETE", "licencias@municanete.cl"),
            ("CHIGUAYANTE", "licencias@chiguayante.cl"),
            ("CONCEPCIÓN", "licencias@concepcion.cl"),
            ("CONTULMO", "licencias@contulmo.cl"),
            ("CORONEL", "licencias@coronel.cl"),
            ("CURANILAHUE", "licencias@curanilahue.cl"),
            ("FLORIDA", "licencias@muniflorida.cl"),
            ("HUALPÉN", "licencias@hualpenciudad.cl"),
            ("HUALQUI", "licencias@munihualqui.cl"),
            ("LAJA", "licencias@munilaja.cl"),
            ("LEBU", "licencias@municodelebu.cl"),
            ("LOS ÁLAMOS", "licencias@munilosalamos.cl"),
            ("LOS ÁNGELES", "licencias@losangeles.cl"),
            ("LOTA", "licencias@lotatransparente.cl"),
            ("MULCHÉN", "licencias@munimulchen.cl"),
            ("NACIMIENTO", "licencias@nacimiento.cl"),
            ("NEGRETE", "licencias@muninegrete.cl"),
            ("PENCO", "licencias@penco.cl"),
            ("QUILACO", "licencias@muniquilaco.cl"),
            ("QUILLECO", "licencias@municipalidadquilleco.cl"),
            ("SAN PEDRO DE LA PAZ", "licencias@sanpedrodelapaz.cl"),
            ("SAN ROSENDO", "licencias@munisanrosendo.cl"),
            ("SANTA BÁRBARA", "licencias@santabarbara.cl"),
            ("SANTA JUANA", "licencias@santajuana.cl"),
            ("TALCAHUANO", "licencias@talcahuano.cl"),
            ("TIRÚA", "licencias@munisdetirua.cl"),
            ("TOMÉ", "licencias@tome.cl"),
            ("TUCAPEL", "licencias@municipalidadtucapel.cl"),
            ("YUMBEL", "licencias@yumbel.cl"),

            // Región de La Araucanía
            ("ANGOL", "licencias@angol.cl"),
            ("CARAHUE", "licencias@carahue.cl"),
            ("CHOLCHOL", "licencias@municholchol.cl"),
            ("COLLIPULLI", "licencias@municollipulli.cl"),
            ("CUNCO", "licencias@municunco.cl"),
            ("CURACAUTÍN", "licencias@curacautin.cl"),
            ("CURARREHUE", "licencias@curarrehue.cl"),
            ("ERCILLA", "licencias@ercilla.cl"),
            ("FREIRE", "licencias@munifreire.cl"),
            ("GALVARINO", "licencias@galvarino.cl"),
            ("GORBEA", "licencias@municipalidadgorbea.cl"),
            ("LAUTARO", "licencias@munilautaro.cl"),
            ("LONCOCHE", "licencias@muniloncoche.cl"),
            ("LONQUIMAY", "licencias@lonquimay.cl"),
            ("LOS SAUCES", "licencias@munilossauces.cl"),
            ("LUMACO", "licencias@munilumaco.cl"),
            ("MELIPEUCO", "licencias@melipueco.cl"),
            ("NUEVA IMPERIAL", "licencias@nuevaimperial.cl"),
            ("PADRE LAS CASAS", "licencias@padrelascasas.cl"),
            ("PERQUENCO", "licencias@perquenco.cl"),
            ("PITRUFQUÉN", "licencias@mpitrufquen.cl"),
            ("PUCÓN", "licencias@municipalidadpucon.cl"),
            ("PURÉN", "licencias@puren.cl"),
            ("RENAICO", "licencias@renaico.cl"),
            ("SAAVEDRA", "licencias@munisaavedra.cl"),
            ("TEMUCO", "licencias@temuco.cl"),
            ("TEODORO SCHMIDT", "licencias@muniteodoro.cl"),
            ("TOLTÉN", "licencias@tolten.cl"),
            ("TRAIGUÉN", "licencias@traiguen.cl"),
            ("VICTORIA", "licencias@victoriachile.cl"),
            ("VILCÚN", "licencias@vilcun.cl"),
            ("VILLARRICA", "licencias@munivillarrica.cl"),

            // Región de Los Ríos
            ("CORRAL", "licencias@municipalidadcorral.cl"),
            ("FUTRONO", "licencias@munifutrono.cl"),
            ("LA UNIÓN", "licencias@munilaunion.cl"),
            ("LAGO RANCO", "licencias@lagoranco.cl"),
            ("LANCO", "licencias@munilanco.cl"),
            ("LOS LAGOS", "licencias@muniloslagos.cl"),
            ("MÁFIL", "licencias@munimafil.cl"),
            ("MARIQUINA", "licencias@munimariquina.cl"),
            ("PAILLACO", "licencias@munipaillaco.cl"),
            ("PANGUIPULLI", "licencias@munipangui.cl"),
            ("RÍO BUENO", "licencias@muniriobueno.cl"),
            ("VALDIVIA", "licencias@munivaldivia.cl"),

            // Región de Los Lagos
            ("ANCUD", "licencias@muniancud.cl"),
            ("CALBUCO", "licencias@municipalidadcalbuco.cl"),
            ("CASTRO", "licencias@castromunicipio.cl"),
            ("CHAITÉN", "licencias@munichaiten.cl"),
            ("CHONCHI", "licencias@municipalidadchonchi.cl"),
            ("COCHAMÓ", "licencias@cochamo.cl"),
            ("CURACO DE VÉLEZ", "licencias@curacodevelez.cl"),
            ("DALCAHUE", "licencias@munidalcahue.cl"),
            ("FRESIA", "licencias@munifresia.cl"),
            ("FRUTILLAR", "licencias@munifrutillar.cl"),
            ("FUTALEUFÚ", "licencias@futaleufu.cl"),
            ("HUALAIHUÉ", "licencias@municipalidadhualaihue.cl"),
            ("LLANQUIHUE", "licencias@llanquihue.cl"),
            ("LOS MUERMOS", "licencias@muermos.cl"),
            ("MAULLÍN", "licencias@municipalidadmaullin.cl"),
            ("OSORNO", "licencias@imo.cl"),
            ("PALENA", "licencias@munipalena.cl"),
            ("PUERTO MONTT", "licencias@puertomontt.cl"),
            ("PUERTO OCTAY", "licencias@puertooctay.cl"),
            ("PUERTO VARAS", "licencias@ptovaras.cl"),
            ("PUQUELDÓN", "licencias@munipuqueldon.cl"),
            ("PURRANQUE", "licencias@purranque.cl"),
            ("PUYEHUE", "licencias@puyehuechile.cl"),
            ("QUEILÉN", "licencias@muniqueilen.cl"),
            ("QUELLÓN", "licencias@muniquellon.cl"),
            ("QUEMCHI", "licencias@quemchi.cl"),
            ("QUINCHAO", "licencias@municipalidadquinchao.cl"),
            ("RÍO NEGRO", "licencias@rionegro.cl"),
            ("SAN JUAN DE LA COSTA", "licencias@sanjuandelacosta.cl"),
            ("SAN PABLO", "licencias@sanpablo.cl"),

            // Región de Aysén
            ("AYSÉN", "licencias@puertoaysen.cl"),
            ("CHILE CHICO", "licencias@chilechico.cl"),
            ("CISNES", "licencias@municipalidadcisnes.cl"),
            ("COCHRANE", "licencias@cochrane.cl"),
            ("COYHAIQUE", "licencias@coyhaique.cl"),
            ("GUAITECAS", "licencias@muniguaitecas.cl"),
            ("LAGO VERDE", "licencias@lagoverdeaysen.cl"),
            ("O'HIGGINS", "licencias@villaohiggins.cl"),
            ("RÍO IBÁÑEZ", "licencias@rioibanez.cl"),
            ("TORTEL", "licencias@municipalidaddetortel.cl"),

            // Región de Magallanes y de la Antártica Chilena
            ("ANTÁRTICA", "licencias@antartica.cl"),
            ("CABO DE HORNOS", "licencias@comunadelcabo.cl"),
            ("LAGUNA BLANCA", "licencias@munilagunablanca.cl"),
            ("NATALES", "licencias@muninatales.cl"),
            ("PORVENIR", "licencias@muniporvenir.cl"),
            ("PRIMAVERA", "licencias@muniprimavera.cl"),
            ("PUNTA ARENAS", "licencias@puntaarenas.cl"),
            ("RÍO VERDE", "licencias@rioverde.cl"),
            ("SAN GREGORIO", "licencias@munisangregorio.cl"),
            ("TIMAUKEL", "licencias@municipalidadtimaukel.cl"),
            ("TORRES DEL PAINE", "licencias@munitorresdelpaine.cl")
        };

        using var transaction = connection.BeginTransaction();
        foreach (var (comuna, email) in defaultList)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO ComunaContact (Comuna, Email) VALUES ($comuna, $email);";
            insert.Parameters.AddWithValue("$comuna", comuna);
            insert.Parameters.AddWithValue("$email", email);
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void Upsert(ComunaContact contact)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ComunaContact (Comuna, Email, Notes)
            VALUES ($comuna, $email, $notes)
            ON CONFLICT(Comuna, Email) DO UPDATE SET Notes = COALESCE($notes, Notes)
            """;
        command.Parameters.AddWithValue("$comuna", contact.Comuna);
        command.Parameters.AddWithValue("$email", contact.Email);
        command.Parameters.AddWithValue("$notes", (object?)contact.Notes ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ComunaContact> All(string? search = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : "WHERE Comuna LIKE $search COLLATE NOCASE OR Email LIKE $search COLLATE NOCASE";
        command.CommandText = $"SELECT * FROM ComunaContact {where} ORDER BY Comuna ASC, Email ASC";
        if (!string.IsNullOrWhiteSpace(search))
        {
            command.Parameters.AddWithValue("$search", $"%{search.Trim()}%");
        }

        var results = new List<ComunaContact>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ComunaContact
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                Comuna = reader.GetString(reader.GetOrdinal("Comuna")),
                Email = reader.GetString(reader.GetOrdinal("Email")),
                Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes"))
            });
        }
        return results;
    }

    public void Update(long id, string comuna, string email, string? notes = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ComunaContact
            SET Comuna = $comuna, Email = $email, Notes = COALESCE($notes, Notes)
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$comuna", comuna.Trim());
        command.Parameters.AddWithValue("$email", email.Trim());
        command.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ComunaContact WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
